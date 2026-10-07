using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace RAG_WaterSupply.Services;

/// <summary>
/// Kiểm duyệt câu SQL do LLM sinh ra trước khi chạy. Hai lớp:
/// 1. Quét văn bản (nhanh): một câu lệnh, chỉ SELECT/CTE, chặn từ khoá ghi / nguy hiểm, chặn metadata hệ thống.
/// 2. Phân tích cú pháp T-SQL thật (ScriptDom): duyệt toàn bộ cây, mọi nguồn dữ liệu — kể cả join bằng dấu phẩy,
///    subquery, APPLY — phải là bảng trong danh sách cho phép (hoặc tên CTE), schema dbo, không truy vấn sang DB khác.
/// Nên kết hợp thêm tài khoản CSDL chỉ có quyền SELECT trên các bảng này (xem database/seed.sql).
/// </summary>
public sealed class SqlSafetyValidator(IEnumerable<string> allowedTables)
{
    private readonly HashSet<string> _allowedTables = new(
        allowedTables,
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex BlockedKeywords = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE|CREATE|ALTER|DROP|EXECUTE|EXEC|GRANT|REVOKE|DENY|BACKUP|RESTORE|DBCC|SHUTDOWN|WAITFOR|KILL|BULK|OPENROWSET|OPENQUERY|OPENDATASOURCE|INTO)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Nguồn dữ liệu được phép xuất hiện trong FROM: bảng có tên, subquery, các kiểu JOIN / APPLY
    private static readonly HashSet<Type> AllowedSources =
    [
        typeof(NamedTableReference),
        typeof(QueryDerivedTable),
        typeof(QualifiedJoin),
        typeof(UnqualifiedJoin),
        typeof(JoinParenthesisTableReference),
    ];

    public SqlValidationResult Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlValidationResult.Fail("Câu SQL rỗng.");
        }

        var normalized = sql.Trim();
        if (normalized.EndsWith(';'))
        {
            normalized = normalized[..^1].TrimEnd();
        }

        // Bỏ nội dung chuỗi '...' trước khi quét, để dữ liệu như N'a; b -- c' không bị bắt nhầm
        var scanText = Regex.Replace(normalized, @"N?'(?:''|[^'])*'", "''");

        if (scanText.Contains(';') || scanText.Contains("--") ||
            scanText.Contains("/*") || scanText.Contains("*/"))
        {
            return SqlValidationResult.Fail("Chỉ cho phép một câu lệnh SQL, không chứa comment.");
        }

        if (!Regex.IsMatch(scanText, @"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase))
        {
            return SqlValidationResult.Fail("Chỉ cho phép SELECT hoặc CTE.");
        }

        var blocked = BlockedKeywords.Match(scanText);
        if (blocked.Success)
        {
            return SqlValidationResult.Fail($"Từ khóa không được phép: {blocked.Value}.");
        }

        if (Regex.IsMatch(scanText, @"\b(sys|INFORMATION_SCHEMA)\s*\.", RegexOptions.IgnoreCase))
        {
            return SqlValidationResult.Fail("Không cho phép truy cập metadata hệ thống.");
        }

        return ValidateSyntaxTree(normalized) is { } error
            ? SqlValidationResult.Fail(error)
            : SqlValidationResult.Ok(normalized);
    }

    private string? ValidateSyntaxTree(string sql)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var tree = parser.Parse(new StringReader(sql), out var parseErrors);
        if (parseErrors.Count > 0)
        {
            return $"Câu SQL không hợp lệ: {parseErrors[0].Message}";
        }

        var statements = ((TSqlScript)tree).Batches.SelectMany(batch => batch.Statements).ToList();
        if (statements is not [SelectStatement select])
        {
            return "Chỉ cho phép đúng một câu SELECT.";
        }

        if (select.Into is not null)
        {
            return "Từ khóa không được phép: INTO.";
        }

        var cteNames = select.WithCtesAndXmlNamespaces?.CommonTableExpressions
            .Select(cte => cte.ExpressionName.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var visitor = new SourceVisitor(_allowedTables, cteNames);
        select.Accept(visitor);
        return visitor.Error;
    }

    /// <summary>Duyệt mọi nguồn dữ liệu trong câu SELECT, dừng ở lỗi đầu tiên.</summary>
    private sealed class SourceVisitor(HashSet<string> allowedTables, HashSet<string> cteNames) : TSqlFragmentVisitor
    {
        public string? Error { get; private set; }

        public override void Visit(TableReference node)
        {
            if (Error is null && !AllowedSources.Contains(node.GetType()))
            {
                Error = $"Nguồn dữ liệu không được phép: {node.GetType().Name}.";
            }
        }

        public override void Visit(NamedTableReference node)
        {
            if (Error is not null) return;

            var name = node.SchemaObject;
            var table = name.BaseIdentifier.Value;

            if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            {
                Error = "Không cho phép truy vấn sang máy chủ hoặc cơ sở dữ liệu khác.";
            }
            else if (name.SchemaIdentifier is { } schema &&
                     !schema.Value.Equals("dbo", StringComparison.OrdinalIgnoreCase))
            {
                Error = $"Schema không được phép: {schema.Value}.";
            }
            else if (!cteNames.Contains(table) && !allowedTables.Contains(table))
            {
                Error = $"Bảng không nằm trong danh sách cho phép: {table}.";
            }
        }
    }
}

public sealed record SqlValidationResult(bool IsValid, string Sql, string? Error)
{
    public static SqlValidationResult Ok(string sql) => new(true, sql, null);
    public static SqlValidationResult Fail(string error) => new(false, string.Empty, error);
}
