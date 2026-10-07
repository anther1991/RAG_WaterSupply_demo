using System.Text.Json;

namespace RAG_WaterSupply.Services;

public static class SseWriter
{
    public static async Task WriteAsync(
        HttpContext context,
        string eventName,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(payload);
        await context.Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }
}
