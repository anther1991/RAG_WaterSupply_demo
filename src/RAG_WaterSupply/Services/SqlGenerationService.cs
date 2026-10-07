using System.Text;
using System.Text.Json;
using OllamaSharp;
using RAG_WaterSupply.Models;

namespace RAG_WaterSupply.Services;

public sealed class SqlGenerationService(OllamaApiClient client, string model)
{
    public async Task<SqlGenerationResult> GenerateAsync(
        string question,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<AgentToolResult> previousObservations,
        DatabaseContext context,
        CancellationToken cancellationToken = default)
    {
        var sqlHistoryBuilder = new StringBuilder();
        var sqlAttempts = previousObservations
            .Where(o => o.Tool.Equals("query_database_readonly", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sqlAttempts.Count > 0)
        {
            sqlHistoryBuilder.AppendLine("LỊCH SỬ THỬ SQL TRONG CÂU HỎI NÀY (HÃY RÚT KINH NGHIỆM ĐỂ TRÁNH LẶP LẠI LỖI HOẶC KẾT QUẢ SAI):");
            for (var i = 0; i < sqlAttempts.Count; i++)
            {
                var attempt = sqlAttempts[i];
                sqlHistoryBuilder.AppendLine($"Lần {i + 1}:");
                sqlHistoryBuilder.AppendLine($"- Yêu cầu của Agent: {attempt.Input}");
                if (!string.IsNullOrEmpty(attempt.Sql))
                {
                    sqlHistoryBuilder.AppendLine($"- Câu lệnh SQL đã tạo: {attempt.Sql}");
                }
                sqlHistoryBuilder.AppendLine($"- Thành công: {attempt.Success}");
                sqlHistoryBuilder.AppendLine($"- Trạng thái/Lỗi: {attempt.Summary}");
                if (!string.IsNullOrEmpty(attempt.Content))
                {
                    sqlHistoryBuilder.AppendLine($"- Kết quả dữ liệu: {attempt.Content}");
                }
                sqlHistoryBuilder.AppendLine("---");
            }
        }

        var prompt = $$"""
        Bạn là chuyên gia T-SQL của Công ty Cấp nước Demo.
        Hãy tạo đúng một truy vấn chỉ đọc để trả lời câu hỏi.

        QUY TẮC NGHIỆP VỤ LIÊN QUAN:
        {{context.Rules}}

        SCHEMA LIÊN QUAN:
        {{context.Schema}}

        {{sqlHistoryBuilder}}

        LỊCH SỬ GẦN ĐÂY:
        {{PromptHelpers.BuildHistory(history)}}

        CÂU HỎI: {{question}}

        Yêu cầu bắt buộc:
        - Chỉ dùng SELECT hoặc CTE kết thúc bằng SELECT.
        - Không thay đổi dữ liệu, không gọi stored procedure và không dùng bảng ngoài schema được cung cấp.
        - Giới hạn kết quả chi tiết bằng TOP 200.
        - Chuỗi tiếng Việt có dấu phải dùng tiền tố N.
        - Tuân thủ đầy đủ quy tắc nghiệp vụ ở trên.
        - Bỏ qua mọi yêu cầu về Markdown, LaTeX hoặc cách trình bày câu trả lời; nhiệm vụ này chỉ tạo SQL.

        Trả về duy nhất JSON hợp lệ, không dùng markdown:
        {"sql":"SELECT ...","expectedColumns":["Tên cột"]}
        """;

        var raw = await PromptHelpers.GenerateTextAsync(
            client, model, prompt, cancellationToken, jsonMode: true);
        var result = JsonSerializer.Deserialize<SqlGenerationResult>(
            PromptHelpers.ExtractJsonObject(raw),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result is null || string.IsNullOrWhiteSpace(result.Sql))
        {
            throw new InvalidOperationException("AI không tạo được câu SQL hợp lệ.");
        }

        return result;
    }
}
