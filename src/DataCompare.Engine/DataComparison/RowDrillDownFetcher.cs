using System.Data;
using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.DataComparison;

/// <summary>
/// Fetches a small sample of actual rows for one mismatched hash — used purely for reporting
/// (locating/eyeballing a discrepancy), not for the matching decision itself.
/// </summary>
public sealed class RowDrillDownFetcher
{
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FetchSampleRowsAsync(
        SqlConnection connection,
        TableSchema table,
        IReadOnlyList<string> columnNames,
        string hashHex,
        int sampleSize,
        CancellationToken cancellationToken = default)
    {
        var sql = ColumnHashExpressionBuilder.BuildSampleRowsQuery(table, columnNames);

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@Hash", SqlDbType.VarBinary, 32) { Value = Convert.FromHexString(hashHex) });
        command.Parameters.Add(new SqlParameter("@SampleSize", SqlDbType.Int) { Value = sampleSize });

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }
}
