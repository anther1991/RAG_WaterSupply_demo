namespace RAG_WaterSupply.Models;

public sealed class AskRequest
{
    public string Question { get; set; } = string.Empty;
    public List<ChatMessage> History { get; set; } = [];
    public List<string>? Images { get; set; }
}

public sealed class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChatMessageContext? Context { get; set; }
}

public sealed class ChatMessageContext
{
    public string? Action { get; set; }
    public string? Intent { get; set; }
    public string? ResolvedQuestion { get; set; }
    public string? Sql { get; set; }
    public int? RowCount { get; set; }
}

public enum QueryIntent
{
    Knowledge,
    Complex,
    Conversation,
    Clarify,
    Unsupported
}

public enum ContextAction
{
    Route,
    AnswerFromHistory,
    Clarify
}

public sealed record RouteResult(
    ContextAction Action,
    QueryIntent Intent,
    string ResolvedQuestion,
    string? DirectAnswer,
    double Confidence,
    string Reason,
    string? ClarificationQuestion,
    string Source,
    long ElapsedMilliseconds);

public sealed class SqlGenerationResult
{
    public string Sql { get; set; } = string.Empty;
    public List<string> ExpectedColumns { get; set; } = [];
}

public sealed record DatabaseResult(
    IReadOnlyList<Dictionary<string, object?>> Rows,
    int RowCount);

public sealed record DatabaseContext(
    string Schema,
    string Rules);

public sealed class AgentDecision
{
    public string Action { get; set; } = string.Empty;
    public string? Tool { get; set; }
    public string? Input { get; set; }
    public string? Answer { get; set; }
}

public sealed record AgentToolResult(
    string Tool,
    string Input,
    string Content,
    string Summary,
    bool Success,
    string? Sql = null,
    int? RowCount = null);
