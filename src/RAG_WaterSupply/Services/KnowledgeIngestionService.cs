using OllamaSharp;
using OllamaSharp.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace RAG_WaterSupply.Services;

public sealed class KnowledgeIngestionService(
    QdrantClient qdrant,
    OllamaApiClient ollama,
    string collectionName,
    string embeddingModel,
    string contentRoot)
{
    public async Task<int> IngestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await qdrant.DeleteCollectionAsync(collectionName, cancellationToken: cancellationToken);
        }
        catch
        {
            // Collection may not exist yet.
        }

        await qdrant.CreateCollectionAsync(
            collectionName,
            new VectorParams { Size = 768, Distance = Distance.Cosine },
            cancellationToken: cancellationToken);
        await qdrant.CreatePayloadIndexAsync(
            collectionName,
            "type",
            PayloadSchemaType.Keyword,
            cancellationToken: cancellationToken);

        var points = new List<PointStruct>();
        ulong id = 1;

        await AddSchemaAsync(Path.Combine(contentRoot, "TruyVanAISchema.md"), points, () => id++, cancellationToken);
        await AddChunkedFileAsync(Path.Combine(contentRoot, "QuyTacNghiepVu.md"), "rules", points, () => id++, cancellationToken);
        await AddHandbookAsync(Path.Combine(contentRoot, "SoTayKhachHang.md"), points, () => id++, cancellationToken);

        if (points.Count > 0)
        {
            await qdrant.UpsertAsync(collectionName, points, cancellationToken: cancellationToken);
        }

        return points.Count;
    }

    private async Task AddSchemaAsync(
        string path,
        List<PointStruct> points,
        Func<ulong> nextId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return;
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        foreach (var table in content.Split("## ", StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var chunk in Split("## " + table.Trim(), 4000))
            {
                points.Add(await CreatePointAsync(nextId(), chunk, "schema", cancellationToken));
            }
        }
    }

    private async Task AddChunkedFileAsync(
        string path,
        string type,
        List<PointStruct> points,
        Func<ulong> nextId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return;
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        foreach (var chunk in Split(content, 4000))
        {
            points.Add(await CreatePointAsync(nextId(), chunk, type, cancellationToken));
        }
    }

    private async Task AddHandbookAsync(
        string path,
        List<PointStruct> points,
        Func<ulong> nextId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return;
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        foreach (var paragraph in content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (paragraph.Length < 10) continue;
            foreach (var chunk in Split(paragraph, 4000))
            {
                points.Add(await CreatePointAsync(nextId(), chunk, "handbook", cancellationToken));
            }
        }
    }

    private async Task<PointStruct> CreatePointAsync(
        ulong id,
        string text,
        string type,
        CancellationToken cancellationToken)
    {
        var embedding = await ollama.EmbedAsync(new EmbedRequest
        {
            Model = embeddingModel,
            Input = [text]
        }, cancellationToken);

        return new PointStruct
        {
            Id = id,
            Vectors = embedding.Embeddings[0],
            Payload = { ["text"] = text, ["type"] = type }
        };
    }

    private static IEnumerable<string> Split(string text, int maxLength)
    {
        for (var index = 0; index < text.Length; index += maxLength)
        {
            yield return text.Substring(index, Math.Min(maxLength, text.Length - index));
        }
    }
}
