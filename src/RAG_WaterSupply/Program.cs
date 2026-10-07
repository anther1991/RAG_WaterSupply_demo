using System.Text.Json;
using System.Text.RegularExpressions;
using OllamaSharp;
using Qdrant.Client;
using RAG_WaterSupply.Models;
using RAG_WaterSupply.Services;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = fileContext =>
    {
        fileContext.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        fileContext.Context.Response.Headers.Pragma = "no-cache";
        fileContext.Context.Response.Headers.Expires = "0";
    }
});

const string collectionName = "IntegratedKnowledge";
var config = builder.Configuration;
var embeddingModel = config["Ollama:EmbeddingModel"] ?? "nomic-embed-text";
var llmModel = config["Ollama:Model"] ?? "gemma4:31b-cloud";

// Chuỗi kết nối nên dùng tài khoản chỉ có quyền SELECT trên các bảng trong schema (xem database/seed.sql)
var sqlConnectionString = config.GetConnectionString("WaterSupply")
    ?? throw new InvalidOperationException("Thiếu ConnectionStrings:WaterSupply (appsettings.json hoặc biến môi trường).");

var qdrant = new QdrantClient(config["Qdrant:Host"] ?? "localhost", int.Parse(config["Qdrant:Port"] ?? "6334"));
var ollama = new OllamaApiClient(new Uri(config["Ollama:Url"] ?? "http://localhost:11434"))
{
    SelectedModel = llmModel
};

var knowledgeRoot = Path.Combine(builder.Environment.ContentRootPath, "Knowledge");
var allowedTables = LoadAllowedTables(Path.Combine(knowledgeRoot, "TruyVanAISchema.md"));

var search = new KnowledgeSearchService(qdrant, ollama, collectionName, embeddingModel);
var router = new QueryRouter(ollama, llmModel, search);
var sqlGenerator = new SqlGenerationService(ollama, llmModel);
var sqlValidator = new SqlSafetyValidator(allowedTables);
var database = new DatabaseQueryService(sqlConnectionString);
var answers = new AnswerComposer(ollama, llmModel);
var geminiApiKey = config["Gemini:ApiKey"] ?? string.Empty;   // để trống = tắt công cụ ask_gemini_expert
var agentTools = new AgentToolRegistry(search, sqlGenerator, sqlValidator, database, ollama, llmModel, geminiApiKey);
var agent = new AgentOrchestrator(ollama, llmModel, agentTools);
var ingestion = new KnowledgeIngestionService(
    qdrant,
    ollama,
    collectionName,
    embeddingModel,
    knowledgeRoot);

var askV2Handler = async (HttpContext context) =>
{
    context.Response.ContentType = "text/event-stream; charset=utf-8";
    context.Response.Headers.CacheControl = "no-cache";
    var cancellationToken = context.RequestAborted;

    try
    {
        var request = await JsonSerializer.DeserializeAsync<AskRequest>(
            context.Request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (request is null || (string.IsNullOrWhiteSpace(request.Question) && (request.Images == null || !request.Images.Any())))
        {
            await SseWriter.WriteAsync(context, "error", new
            {
                message = "Vui lòng nhập câu hỏi hoặc đính kèm hình ảnh."
            }, cancellationToken);
            return;
        }

        var question = string.IsNullOrWhiteSpace(request.Question) && request.Images != null && request.Images.Any()
            ? "Mô tả hình ảnh này theo thông tin từ hình ảnh bên dưới"
            : request.Question.Trim();
        var history = request.History ?? [];
        var showTechnicalDetails = context.Request.Query.TryGetValue("debug", out var debugValue) &&
            debugValue.Any(value => value == "1");

        if (request.Images != null && request.Images.Any())
        {
            await SseWriter.WriteAsync(context, "status", new
            {
                message = "Đang phân tích hình ảnh..."
            }, cancellationToken);

            var imagePrompt = """
                Bạn là chuyên gia thị giác máy tính của Công ty Cấp nước Demo.
                Hãy mô tả chi tiết hình ảnh này, đặc biệt tập trung trích xuất chính xác:
                - Các chữ viết, biển hiệu, nhãn danh bộ, mã số khách hàng (nếu có).
                - Các chữ số, chỉ số hiển thị trên mặt đồng hồ nước (nếu có).
                - Các biểu hiện sự cố: rò rỉ, nứt vỡ đường ống, van bị hỏng (nếu có).
                Nếu không thể nhận diện được thông tin gì rõ ràng, hãy ghi rõ không thể nhận diện.
                Trả lời ngắn gọn, trực diện, bằng tiếng Việt.
                """;

            var imageDescription = await PromptHelpers.GenerateTextAsync(
                ollama, llmModel, imagePrompt, cancellationToken, false, request.Images);

            if (!string.IsNullOrWhiteSpace(imageDescription))
            {
                question = $"{question}\n[Thông tin từ hình ảnh: {imageDescription}]";
            }
        }

        await SseWriter.WriteAsync(context, "status", new
        {
            message = "Đang xác định loại yêu cầu..."
        }, cancellationToken);

        var route = await router.ClassifyAsync(question, history, cancellationToken);
        var resolvedQuestion = string.IsNullOrWhiteSpace(route.ResolvedQuestion)
            ? question
            : route.ResolvedQuestion;
        await SseWriter.WriteAsync(context, "route", new
        {
            intent = route.Action == ContextAction.AnswerFromHistory
                ? "HISTORY"
                : route.Intent.ToString().ToUpperInvariant(),
            semanticIntent = route.Intent.ToString().ToUpperInvariant(),
            action = ToActionName(route.Action),
            resolvedQuestion,
            confidence = route.Confidence,
            reason = route.Reason,
            source = route.Source,
            elapsedMs = route.ElapsedMilliseconds
        }, cancellationToken);

        if (route.Action == ContextAction.AnswerFromHistory)
        {
            await SseWriter.WriteAsync(context, "answer_delta", new
            {
                text = route.DirectAnswer
                    ?? "Tôi chưa tìm thấy đủ thông tin trong lịch sử để trả lời chắc chắn."
            }, cancellationToken);
        }
        else if (route.Action == ContextAction.Clarify)
        {
            await SseWriter.WriteAsync(context, "answer_delta", new
            {
                text = route.ClarificationQuestion
                    ?? "Bạn vui lòng cung cấp thêm thông tin để tôi có thể tra cứu chính xác."
            }, cancellationToken);
        }
        else
        {
            switch (route.Intent)
            {
                case QueryIntent.Conversation:
                    await foreach (var text in answers.StreamConversationAnswerAsync(
                        resolvedQuestion, history, cancellationToken))
                    {
                        await SseWriter.WriteAsync(context, "answer_delta", new { text }, cancellationToken);
                    }
                    break;

                case QueryIntent.Knowledge:
                    await RunKnowledgePipelineAsync(
                        context, resolvedQuestion, history, search, answers, agent, showTechnicalDetails, cancellationToken);
                    break;

                case QueryIntent.Complex:
                    await RunAgentPipelineAsync(
                        context, resolvedQuestion, history, request.Images, agent, showTechnicalDetails, cancellationToken);
                    break;

                case QueryIntent.Clarify:
                    await SseWriter.WriteAsync(context, "answer_delta", new
                    {
                        text = route.ClarificationQuestion
                            ?? "Bạn vui lòng cung cấp thêm thông tin để tôi có thể tra cứu chính xác."
                    }, cancellationToken);
                    break;

                default:
                    await SseWriter.WriteAsync(context, "answer_delta", new
                    {
                        text = "Yêu cầu này nằm ngoài phạm vi kiến thức và dữ liệu hiện có của hệ thống."
                    }, cancellationToken);
                    break;
            }
        }

        await SseWriter.WriteAsync(context, "done", new { success = true }, cancellationToken);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // The browser closed the request.
    }
    catch (Exception exception)
    {
        await SseWriter.WriteAsync(context, "error", new
        {
            message = $"Lỗi hệ thống: {exception.Message}"
        }, CancellationToken.None);
    }
};

app.MapPost("/api/ask-v2", askV2Handler);

// Compatibility response for browser tabs that still have the pre-v2 JavaScript loaded.
// The legacy client expects each SSE data payload to be a JSON string, not an object.
app.MapPost("/api/ask", async (HttpContext context) =>
{
    context.Response.ContentType = "text/event-stream; charset=utf-8";
    context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    var message = "Ứng dụng vừa được cập nhật. Vui lòng tải lại trang rồi gửi câu hỏi lần nữa.";
    await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(message)}\n\n", context.RequestAborted);
    await context.Response.Body.FlushAsync(context.RequestAborted);
});

app.MapPost("/api/ingest", async (HttpContext context) =>
{
    var count = await ingestion.IngestAsync(context.RequestAborted);
    return Results.Ok(new
    {
        success = true,
        pointCount = count,
        message = $"Đã nạp {count} mẫu tri thức."
    });
});

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    model = llmModel,
    collection = collectionName
}));

app.Run();

static async Task RunKnowledgePipelineAsync(
    HttpContext context,
    string question,
    IReadOnlyList<ChatMessage> history,
    KnowledgeSearchService search,
    AnswerComposer answers,
    AgentOrchestrator agent,
    bool showTechnicalDetails,
    CancellationToken cancellationToken)
{
    await SseWriter.WriteAsync(context, "status", new
    {
        message = "Đang tìm trong sổ tay dịch vụ..."
    }, cancellationToken);

    var knowledge = await search.SearchHandbookAsync(question, cancellationToken);
    
    var enumerator = answers.StreamKnowledgeAnswerAsync(question, history, knowledge, cancellationToken).GetAsyncEnumerator(cancellationToken);
    var firstChunks = new List<string>();
    var accumulated = "";
    var isNotFound = false;

    try
    {
        while (accumulated.Length < 15 && await enumerator.MoveNextAsync())
        {
            var chunk = enumerator.Current;
            firstChunks.Add(chunk);
            accumulated += chunk;
        }

        if (accumulated.TrimStart().StartsWith("[NOT_FOUND]", StringComparison.OrdinalIgnoreCase))
        {
            isNotFound = true;
        }
    }
    finally
    {
        if (isNotFound)
        {
            await enumerator.DisposeAsync();
        }
    }

    if (isNotFound)
    {
        await SseWriter.WriteAsync(context, "status", new
        {
            message = "Không tìm thấy trong sổ tay. Đang chuyển hướng sang Agent..."
        }, cancellationToken);

        await RunAgentPipelineAsync(
            context, question, history, null, agent, showTechnicalDetails, cancellationToken);
        return;
    }

    foreach (var chunk in firstChunks)
    {
        await SseWriter.WriteAsync(context, "answer_delta", new { text = chunk }, cancellationToken);
    }

    try
    {
        while (await enumerator.MoveNextAsync())
        {
            await SseWriter.WriteAsync(context, "answer_delta", new { text = enumerator.Current }, cancellationToken);
        }
    }
    finally
    {
        await enumerator.DisposeAsync();
    }
}

static Task RunAgentPipelineAsync(
    HttpContext context,
    string question,
    IReadOnlyList<ChatMessage> history,
    IReadOnlyList<string>? images,
    AgentOrchestrator agent,
    bool showTechnicalDetails,
    CancellationToken cancellationToken)
{
    return agent.RunAsync(
        question,
        history,
        images,
        (eventName, payload) =>
        {
            if (eventName == "sql" && !showTechnicalDetails)
            {
                return Task.CompletedTask;
            }

            return SseWriter.WriteAsync(
                context, eventName, payload, cancellationToken);
        },
        cancellationToken);
}


static IReadOnlyCollection<string> LoadAllowedTables(string schemaPath)
{
    if (!File.Exists(schemaPath)) return [];

    return File.ReadLines(schemaPath)
        .Select(line => Regex.Match(line, @"^##\s+([A-Za-z0-9_]+)\s*$"))
        .Where(match => match.Success)
        .Select(match => match.Groups[1].Value)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

static string ToActionName(ContextAction action) => action switch
{
    ContextAction.AnswerFromHistory => "ANSWER_FROM_HISTORY",
    ContextAction.Clarify => "CLARIFY",
    _ => "ROUTE"
};


