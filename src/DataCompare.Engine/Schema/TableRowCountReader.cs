using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Schema;

/// <summary>
/// Reads approximate row counts for every table in one round trip, via <c>sys.partitions</c> —
/// instant regardless of table size, since it reads stored metadata rather than scanning rows. Used
/// to classify tables as "large" before a data comparison starts (planning.md §19), so a handful of
/// huge tables can be deferred and parallelized instead of competing for a worker slot for their
/// entire run.
/// </summary>
public sealed class TableRowCountReader
{
    private const string Query = """
        SELECT s.name + '.' + t.name AS TableName, SUM(p.rows) AS ApproxRowCount
        FROM sys.tables t
        JOIN sys.schemas s ON t.schema_id = s.schema_id
        JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
        GROUP BY s.name, t.name;
        """;

    /// <summary>
    /// Reads the approximate row count for every table in the connected database.
    /// </summary>
    /// <param name="connection">an open Microsoft.Data.SqlClient.SqlConnection to query</param>
    /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the read</param>
    /// <returns>returns a System.Collections.Generic.IReadOnlyDictionary of System.String (schema-qualified table name) to System.Int64 (approximate row count)</returns>
    public async Task<IReadOnlyDictionary<string, long>> ReadApproximateRowCountsAsync(
        SqlConnection connection, CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        await using var command = new SqlCommand(Query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = reader.GetInt64(1);
        }

        return counts;
    }
}
