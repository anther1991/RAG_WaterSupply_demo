using System.Text;
using OllamaSharp;
using OllamaSharp.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using RAG_WaterSupply.Models;

namespace RAG_WaterSupply.Services;

public sealed class KnowledgeSearchService(
    QdrantClient qdrant,
    OllamaApiClient ollama,
    string collectionName,
    string embeddingModel)
{
    public async Task<string> SearchHandbookAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        var results = await SearchByTypeAsync(question, "handbook", 10, cancellationToken);
        return JoinPayloadText(results);
    }

    public async Task<string> SearchBusinessRulesAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        var results = await ScrollByTypeAsync("rules", cancellationToken);
        return JoinPayloadText(results);
    }

    public async Task<DatabaseContext> SearchDatabaseContextAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        var schemaTask = ScrollByTypeAsync("schema", cancellationToken);
        var rulesTask = ScrollByTypeAsync("rules", cancellationToken);
        await Task.WhenAll(schemaTask, rulesTask);

        return new DatabaseContext(
            JoinPayloadText(await schemaTask),
            JoinPayloadText(await rulesTask));
    }

    private async Task<IReadOnlyList<RetrievedPoint>> ScrollByTypeAsync(
        string type,
        CancellationToken cancellationToken)
    {
        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "type",
                        Match = new Match { Keyword = type }
                    }
                }
            }
        };

        var response = await qdrant.ScrollAsync(
            collectionName,
            filter: filter,
            limit: 1000,
            cancellationToken: cancellationToken);

        return response.Result;
    }

    private async Task<IReadOnlyList<ScoredPoint>> SearchByTypeAsync(
        string question,
        string type,
        ulong limit,
        CancellationToken cancellationToken)
    {
        var embedding = await ollama.EmbedAsync(new EmbedRequest
        {
            Model = embeddingModel,
            Input = [question]
        }, cancellationToken);

        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "type",
                        Match = new Match { Keyword = type }
                    }
                }
            }
        };

        return await qdrant.SearchAsync(
            collectionName,
            embedding.Embeddings[0],
            filter: filter,
            limit: limit,
            cancellationToken: cancellationToken);
    }

    private static string JoinPayloadText(IEnumerable<ScoredPoint> points)
    {
        var output = new StringBuilder();
        foreach (var point in points)
        {
            if (point.Payload.TryGetValue("text", out var value))
            {
                output.AppendLine(value.StringValue);
            }
        }

        return output.ToString();
    }

    private static string JoinPayloadText(IEnumerable<RetrievedPoint> points)
    {
        var output = new StringBuilder();
        foreach (var point in points)
        {
            if (point.Payload.TryGetValue("text", out var value))
            {
                output.AppendLine(value.StringValue);
            }
        }

        return output.ToString();
    }
}

