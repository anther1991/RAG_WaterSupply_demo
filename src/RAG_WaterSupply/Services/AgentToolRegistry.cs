using System.Net.Http;
using System.Text.Json;
using OllamaSharp;
using RAG_WaterSupply.Models;

namespace RAG_WaterSupply.Services;

public sealed class AgentToolRegistry(
    KnowledgeSearchService search,
    SqlGenerationService sqlGenerator,
    SqlSafetyValidator sqlValidator,
    DatabaseQueryService database,
    OllamaApiClient ollama,
    string llmModel,
    string geminiApiKey)
{
    public KnowledgeSearchService Search => search;

    public static readonly IReadOnlySet<string> AllowedTools = new HashSet<string>(
        ["search_knowledge", "query_database_readonly", "view_image", "ask_gemini_expert"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<AgentToolResult> ExecuteAsync(
        string tool,
        string input,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<string>? images,
        IReadOnlyList<AgentToolResult> previousObservations,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedTools.Contains(tool))
        {
            return new AgentToolResult(
                tool, input, string.Empty, $"Tool không được phép: {tool}.", false);
        }

        try
        {
            return tool.ToLowerInvariant() switch
            {
                "search_knowledge" => await SearchKnowledgeAsync(input, cancellationToken),
                "query_database_readonly" => await QueryDatabaseAsync(input, history, previousObservations, cancellationToken),
                "view_image" => await ViewImageAsync(input, images, cancellationToken),
                "ask_gemini_expert" => await AskGeminiExpertAsync(input, images, cancellationToken),
                _ => new AgentToolResult(tool, input, string.Empty, "Tool không được hỗ trợ.", false)
            };
        }
        catch (Exception exception)
        {
            return new AgentToolResult(
                tool,
                input,
                string.Empty,
                $"Tool thất bại: {exception.Message}",
                false);
        }
    }

    private async Task<AgentToolResult> SearchKnowledgeAsync(
        string input,
        CancellationToken cancellationToken)
    {
        var content = await search.SearchHandbookAsync(input, cancellationToken);
        var found = !string.IsNullOrWhiteSpace(content);
        return new AgentToolResult(
            "search_knowledge",
            input,
            LimitContent(content, 40_000),
            found ? "Đã tìm thấy kiến thức sổ tay liên quan." : "Không tìm thấy kiến thức phù hợp.",
            found);
    }


    private async Task<AgentToolResult> QueryDatabaseAsync(
        string input,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<AgentToolResult> previousObservations,
        CancellationToken cancellationToken)
    {
        var context = await search.SearchDatabaseContextAsync(input, cancellationToken);
        var generated = await sqlGenerator.GenerateAsync(input, history, previousObservations, context, cancellationToken);
        var validation = sqlValidator.Validate(generated.Sql);

        if (!validation.IsValid)
        {
            return new AgentToolResult(
                "query_database_readonly",
                input,
                string.Empty,
                $"SQL bị từ chối: {validation.Error}",
                false);
        }

        var result = await database.ExecuteAsync(validation.Sql, cancellationToken);
        var content = JsonSerializer.Serialize(new
        {
            rowCount = result.RowCount,
            rows = result.Rows.Take(50)
        }, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return new AgentToolResult(
            "query_database_readonly",
            input,
            LimitContent(content, 16_000),
            $"Truy vấn database thành công, nhận {result.RowCount} dòng.",
            true,
            validation.Sql,
            result.RowCount);
    }

    private async Task<AgentToolResult> ViewImageAsync(
        string input,
        IReadOnlyList<string>? images,
        CancellationToken cancellationToken)
    {
        if (images == null || !images.Any())
        {
            return new AgentToolResult(
                "view_image",
                input,
                string.Empty,
                "Không có hình ảnh nào được đính kèm trong cuộc hội thoại này.",
                false);
        }

        var prompt = $$"""
            Bạn là chuyên gia thị giác máy tính của Công ty Cấp nước Demo.
            Đọc kỹ hình ảnh và tập trung trả lời chi tiết, chính xác yêu cầu sau đây:
            {{input}}
            Trả lời ngắn gọn, trực diện, bằng tiếng Việt.
            """;

        var response = await PromptHelpers.GenerateTextAsync(
            ollama, llmModel, prompt, cancellationToken, jsonMode: false, images);

        var success = !string.IsNullOrWhiteSpace(response);
        return new AgentToolResult(
            "view_image",
            input,
            response,
            success ? "Đã trích xuất thông tin chi tiết từ hình ảnh." : "Không thể phân tích được hình ảnh.",
            success);
    }

    private static string LimitContent(string content, int maxCharacters = 10_000)
    {
        if (content.Length <= maxCharacters) return content;
        return content[..maxCharacters] + "\n[Đã rút gọn kết quả]";
    }

    private static readonly HttpClient _httpClient = new();

    private async Task<AgentToolResult> AskGeminiExpertAsync(
        string input,
        IReadOnlyList<string>? images,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            return new AgentToolResult(
                "ask_gemini_expert",
                input,
                string.Empty,
                "Gemini API Key chưa được cấu hình.",
                false);
        }

        try
        {
            // API key gửi qua header thay vì query string để không lọt vào log / proxy
            const string url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";

            var parts = new List<object>
            {
                new { text = input }
            };

            if (images != null)
            {
                foreach (var base64Image in images)
                {
                    parts.Add(new
                    {
                        inlineData = new
                        {
                            mimeType = "image/jpeg",
                            data = base64Image
                        }
                    });
                }
            }

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts }
                },
                tools = new[]
                {
                    new { googleSearch = new { } }
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("x-goog-api-key", geminiApiKey);
            request.Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new AgentToolResult(
                    "ask_gemini_expert",
                    input,
                    string.Empty,
                    $"Gọi Gemini API thất bại với mã {response.StatusCode}: {responseBody}",
                    false);
            }

            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && 
                candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];
                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var responseParts) &&
                    responseParts.GetArrayLength() > 0)
                {
                    var text = responseParts[0].GetProperty("text").GetString() ?? string.Empty;
                    return new AgentToolResult(
                        "ask_gemini_expert",
                        input,
                        text,
                        "Đã nhận phản hồi từ chuyên gia Gemini Cloud.",
                        true);
                }
            }

            return new AgentToolResult(
                "ask_gemini_expert",
                input,
                responseBody,
                "Không có câu trả lời nào từ Gemini Cloud.",
                false);
        }
        catch (Exception ex)
        {
            return new AgentToolResult(
                "ask_gemini_expert",
                input,
                string.Empty,
                $"Lỗi khi gọi Gemini API: {ex.Message}",
                false);
        }
    }
}
