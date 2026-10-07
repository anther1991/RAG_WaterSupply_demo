using System.Text;
using System.Text.Json;
using OllamaSharp;
using OllamaSharp.Models;
using RAG_WaterSupply.Models;

namespace RAG_WaterSupply.Services;

internal static class PromptHelpers
{
    public static async Task<string> GenerateTextAsync(
        OllamaApiClient client,
        string model,
        string prompt,
        CancellationToken cancellationToken = default,
        bool jsonMode = false,
        IEnumerable<string>? images = null)
    {
        var output = new StringBuilder();
        await foreach (var part in client.GenerateAsync(new GenerateRequest
        {
            Prompt = prompt,
            Model = model,
            Stream = false,
            Format = jsonMode ? "json" : null,
            Images = images?.ToArray()
        }, cancellationToken))
        {
            output.Append(part?.Response ?? string.Empty);
        }

        return output.ToString().Trim();
    }

    public static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new JsonException("AI không trả về JSON hợp lệ.");
        }

        return text[start..(end + 1)];
    }

    public static string BuildHistory(IEnumerable<ChatMessage> history, int maxMessages = 6)
    {
        var lines = history
            .TakeLast(maxMessages)
            .Select(message =>
            {
                var role = message.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                    ? "Người dùng"
                    : "Trợ lý";
                var rawContent = message.Content ?? string.Empty;
                var content = rawContent.Length > 1500
                    ? rawContent[..1500]
                    : rawContent;
                var line = $"{role}: {content}";

                if (!message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ||
                    message.Context is null)
                {
                    return line;
                }

                var metadata = new List<string>();
                AddMetadata(metadata, "action", message.Context.Action, 40);
                AddMetadata(metadata, "intent", message.Context.Intent, 40);
                AddMetadata(metadata, "câu hỏi độc lập", message.Context.ResolvedQuestion, 800);
                AddMetadata(metadata, "SQL đã thực thi", message.Context.Sql, 2500);
                if (message.Context.RowCount is int rowCount)
                {
                    metadata.Add($"số dòng={rowCount}");
                }

                return metadata.Count == 0
                    ? line
                    : $"{line}\n[Ngữ cảnh nội bộ của lượt trả lời: {string.Join("; ", metadata)}]";
            });

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddMetadata(
        ICollection<string> metadata,
        string name,
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var normalized = value.ReplaceLineEndings(" ").Trim();
        if (normalized.Length > maxLength)
        {
            normalized = normalized[..maxLength];
        }
        metadata.Add($"{name}={normalized}");
    }
}
