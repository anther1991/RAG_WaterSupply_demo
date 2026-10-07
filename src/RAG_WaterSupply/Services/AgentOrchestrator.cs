using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OllamaSharp;
using RAG_WaterSupply.Models;

namespace RAG_WaterSupply.Services;

public sealed class AgentOrchestrator(
    OllamaApiClient client,
    string model,
    AgentToolRegistry tools)
{
    private const int MaxSteps = 10;

    public async Task RunAsync(
        string question,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<string>? images,
        Func<string, object, Task> emitAsync,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(150));
        var token = timeout.Token;

        var observations = new List<AgentToolResult>();
        var callSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var businessRules = await tools.Search.SearchBusinessRulesAsync(question, token);
        var totalStopwatch = Stopwatch.StartNew();

        for (var step = 1; step <= MaxSteps; step++)
        {
            await emitAsync("agent_step", new
            {
                step,
                maxSteps = MaxSteps,
                status = "Đang quyết định bước tiếp theo..."
            });

            var decision = await DecideAsync(question, history, observations, step, businessRules, token);
            var action = (decision?.Action ?? "invalid_json").Trim().ToLowerInvariant();

            if (action == "invalid_json")
            {
                var rejected = new AgentToolResult(
                    "system",
                    "json",
                    $"Định dạng phản hồi không hợp lệ: {decision?.Answer}. Vui lòng chỉ trả về duy nhất một đối tượng JSON tuân thủ chính xác cấu trúc mẫu.",
                    "Lỗi định dạng JSON.",
                    false);
                observations.Add(rejected);
                await EmitGuardResultAsync(emitAsync, step, rejected);
                continue;
            }

            if (action == "finish")
            {
                var answer = string.IsNullOrWhiteSpace(decision!.Answer)
                    ? "Tôi chưa có đủ thông tin để trả lời chắc chắn."
                    : decision.Answer;
                await emitAsync("answer_delta", new { text = answer });
                await emitAsync("agent_done", new
                {
                    steps = step,
                    toolCalls = observations.Count,
                    elapsedMs = totalStopwatch.ElapsedMilliseconds
                });
                return;
            }

            if (action == "clarify")
            {
                var clarification = string.IsNullOrWhiteSpace(decision!.Answer)
                    ? "Bạn vui lòng cung cấp thêm thông tin để tôi tiếp tục."
                    : decision.Answer;
                await emitAsync("answer_delta", new { text = clarification });
                await emitAsync("agent_done", new
                {
                    steps = step,
                    toolCalls = observations.Count,
                    elapsedMs = totalStopwatch.ElapsedMilliseconds,
                    requiresClarification = true
                });
                return;
            }

            if (action != "use_tool")
            {
                var rejected = new AgentToolResult(
                    "system",
                    action,
                    $"Hành động '{action}' không hợp lệ. Bạn chỉ được phép chọn một trong ba hành động: 'use_tool', 'clarify', hoặc 'finish'.",
                    $"Hành động '{action}' không hợp lệ.",
                    false);
                observations.Add(rejected);
                await EmitGuardResultAsync(emitAsync, step, rejected);
                continue;
            }

            if (string.IsNullOrWhiteSpace(decision!.Tool))
            {
                var rejected = new AgentToolResult(
                    "system",
                    "use_tool",
                    "Bạn chọn hành động 'use_tool' nhưng chưa cung cấp tên công cụ trong thuộc tính 'tool'. Vui lòng chọn một công cụ hợp lệ.",
                    "Thiếu tên công cụ.",
                    false);
                observations.Add(rejected);
                await EmitGuardResultAsync(emitAsync, step, rejected);
                continue;
            }

            var tool = decision.Tool.Trim();
            var input = string.IsNullOrWhiteSpace(decision.Input) ? question : decision.Input.Trim();

            if (!AgentToolRegistry.AllowedTools.Contains(tool))
            {
                var rejected = new AgentToolResult(
                    tool, input, string.Empty, "Tool không nằm trong allowlist.", false);
                observations.Add(rejected);
                await EmitGuardResultAsync(emitAsync, step, rejected);
                continue;
            }

            var signature = $"{tool}:{input}";
            if (!callSignatures.Add(signature))
            {
                var rejected = new AgentToolResult(
                    tool, input, string.Empty, "Không gọi lại tool với cùng tham số.", false);
                observations.Add(rejected);
                await EmitGuardResultAsync(emitAsync, step, rejected);
                continue;
            }

            await emitAsync("tool_call", new { step, tool, input });
            var result = await tools.ExecuteAsync(tool, input, history, images, observations, token);
            observations.Add(result);

            if (!string.IsNullOrWhiteSpace(result.Sql))
            {
                await emitAsync("sql", new { query = result.Sql });
            }

            await emitAsync("tool_result", new
            {
                step,
                tool,
                success = result.Success,
                summary = result.Summary,
                rowCount = result.RowCount
            });
        }

        await emitAsync("answer_delta", new
        {
            text = "Tôi đã đạt giới hạn số bước nhưng chưa thể hoàn tất yêu cầu một cách chắc chắn."
        });
        await emitAsync("agent_done", new
        {
            steps = MaxSteps,
            toolCalls = observations.Count,
            elapsedMs = totalStopwatch.ElapsedMilliseconds,
            limitReached = true
        });
    }

    private static Task EmitGuardResultAsync(
        Func<string, object, Task> emitAsync,
        int step,
        AgentToolResult result)
    {
        return emitAsync("tool_result", new
        {
            step,
            tool = result.Tool,
            success = false,
            summary = result.Summary,
            guardrail = true
        });
    }

    private async Task<AgentDecision> DecideAsync(
        string question,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<AgentToolResult> observations,
        int step,
        string businessRules,
        CancellationToken cancellationToken)
    {
        var observationText = new StringBuilder();
        foreach (var observation in observations)
        {
            observationText.AppendLine($"TOOL: {observation.Tool}");
            observationText.AppendLine($"INPUT: {observation.Input}");
            observationText.AppendLine($"SUCCESS: {observation.Success}");
            observationText.AppendLine($"SUMMARY: {observation.Summary}");
            observationText.AppendLine($"RESULT: {observation.Content}");
            observationText.AppendLine("---");
        }

        var prompt = $$"""
        Bạn là bộ điều phối tác vụ nội bộ của Công ty Cấp nước Demo.
        Hãy chọn đúng một hành động tiếp theo. Không trình bày suy luận nội bộ.

        CÔNG CỤ ĐƯỢC PHÉP:
        - search_knowledge: tìm trong sổ tay dịch vụ khách hàng (quy trình, chính sách, câu hỏi thường gặp).
        - query_database_readonly: tra cứu hoặc thống kê dữ liệu thực tế bằng SQL chỉ đọc.
        - view_image: xem lại hình ảnh người dùng đã tải lên trong cuộc trò chuyện. Sử dụng khi bạn cần mô tả chi tiết thông tin cụ thể từ ảnh gốc mà bạn cần kiểm chứng. Input là yêu cầu mô tả chi tiết bạn muốn tập trung quan sát trên ảnh.
        - ask_gemini_expert: hỏi ý kiến của mô hình AI cao cấp Google Gemini. Sử dụng công cụ này khi bạn gặp các tác vụ cực kỳ phức tạp (như phân tích hình ảnh chuyên sâu cần độ chính xác cao, giải quyết các mâu thuẫn dữ liệu khó, hoặc cần lập luận logic cấp độ cao). Hoặc vấn đề/khái niệm/từ khóa nằm ngoài sự hiểu biết mà kể cả tìm kiếm search_knowledge cũng không có kết quả. Input là câu hỏi chi tiết và tiêu điểm bạn muốn chuyên gia Gemini tập trung phân tích, tìm kiếm.

        QUY TẮC:
        - Chỉ dùng dữ liệu trong kết quả tool; không tự tạo số liệu.
        - Nếu tool trả SUCCESS=true, phải đọc kỹ RESULT trước khi kết luận không có thông tin.
        - Có thể gọi nhiều tool nếu câu hỏi cần kết hợp nhiều nguồn.
        - Không gọi lại cùng tool với cùng input.
        - Input của tool phải là yêu cầu bằng ngôn ngữ tự nhiên, không phải câu SQL.
        - Nếu đủ dữ liệu, chọn finish và trả lời tiếng Việt rõ ràng.
        - Nếu thiếu thông tin mà tool không thể giải quyết, chọn clarify.
        - Câu trả lời cuối dùng Markdown; dùng bảng Markdown khi cần so sánh.
        - Công thức toán dùng $...$ hoặc $$...$$ theo cú pháp LaTeX; không tạo HTML trực tiếp.
        - Đặt nhãn tiếng Việt bên ngoài công thức; bên trong LaTeX chỉ dùng ký hiệu, số và tên biến ASCII.
        - Nếu câu trả lời mô tả quy trình, luồng xử lý hoặc mối quan hệ phức tạp, hãy vẽ sơ đồ bằng Mermaid.js sử dụng khối mã ```mermaid ... ```. Sơ đồ Mermaid nên dùng định dạng flowchart TD hoặc flowchart LR. Chỉ dùng nhãn tiếng Việt hoặc ký tự ASCII chuẩn bên trong các hộp sơ đồ.

        QUY TẮC NGHIỆP VỤ LIÊN QUAN:
        {{businessRules}}

        LỊCH SỬ GẦN ĐÂY:
        {{PromptHelpers.BuildHistory(history)}}

        MỤC TIÊU:
        {{question}}

        KẾT QUẢ CÁC BƯỚC TRƯỚC:
        {{observationText}}

        BƯỚC HIỆN TẠI: {{step}}/{{MaxSteps}}

        Trả về duy nhất một JSON:
        - Gọi tool: {"action":"use_tool","tool":"search_knowledge","input":"nội dung cần tìm","answer":null}
        - Hỏi lại: {"action":"clarify","tool":null,"input":null,"answer":"câu hỏi cần người dùng bổ sung"}
        - Hoàn tất: {"action":"finish","tool":null,"input":null,"answer":"câu trả lời cuối cùng"}
        """;

        try
        {
            var raw = await PromptHelpers.GenerateTextAsync(
                client, model, prompt, cancellationToken, jsonMode: true);
            var decision = JsonSerializer.Deserialize<AgentDecision>(
                PromptHelpers.ExtractJsonObject(raw),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (decision is null || string.IsNullOrWhiteSpace(decision.Action))
            {
                return new AgentDecision
                {
                    Action = "invalid_json",
                    Answer = "Không thể phân tích phản hồi thành JSON hợp lệ hoặc thiếu trường 'action'."
                };
            }

            return decision;
        }
        catch (Exception exception)
        {
            return new AgentDecision
            {
                Action = "invalid_json",
                Answer = $"Lỗi phân tích cú pháp: {exception.Message}"
            };
        }
    }
}
