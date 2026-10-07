using Microsoft.Data.SqlClient;
using RAG_WaterSupply.Models;

namespace RAG_WaterSupply.Services;

public sealed class DatabaseQueryService(string connectionString)
{
    public async Task<DatabaseResult> ExecuteAsync(
        string sql,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // ROWCOUNT is applied by trusted application code, not supplied by the model.
        await using var command = new SqlCommand($"SET ROWCOUNT 200;\n{sql}", connection)
        {
            CommandTimeout = 30
        };

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();

        while (rows.Count < 200 && await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = await reader.IsDBNullAsync(index, cancellationToken)
                    ? null
                    : reader.GetValue(index);
            }
            rows.Add(row);
        }

        return new DatabaseResult(rows, rows.Count);
    }
}
