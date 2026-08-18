using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.DataComparison;

/// <summary>
/// Row-level data diff via server-side content hashing (planning.md §6): pulls (hash, count) pairs
/// rather than full rows, then diffs the two multisets in-memory — scales to large tables because
/// only a few dozen bytes per distinct row content travel back, not the row itself.
/// </summary>
public sealed class TableHashComparer
{
    public async Task<Dictionary<string, long>> ReadHashCountsAsync(
        SqlConnection connection,
        TableSchema table,
        IReadOnlyList<string> columnNames,
        CancellationToken cancellationToken = default)
    {
        var sql = ColumnHashExpressionBuilder.BuildGroupedCountQuery(table, columnNames);
        var counts = new Dictionary<string, long>();

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var hashBytes = (byte[])reader.GetValue(0);
            counts[Convert.ToHexString(hashBytes)] = reader.GetInt64(1);
        }

        return counts;
    }

    /// <summary>Pure multiset diff — no DB access, fully unit-testable.</summary>
    public TableDataDiffResult Diff(
        string tableName,
        IReadOnlyDictionary<string, long> sourceCounts,
        IReadOnlyDictionary<string, long> targetCounts)
    {
        var allHashes = new HashSet<string>(sourceCounts.Keys);
        allHashes.UnionWith(targetCounts.Keys);

        long matched = 0;
        var discrepancies = new List<HashCountDiscrepancy>();
        foreach (var hash in allHashes)
        {
            sourceCounts.TryGetValue(hash, out var sourceCount);
            targetCounts.TryGetValue(hash, out var targetCount);
            matched += Math.Min(sourceCount, targetCount);

            if (sourceCount != targetCount)
            {
                discrepancies.Add(new HashCountDiscrepancy(hash, sourceCount, targetCount));
            }
        }

        discrepancies.Sort((a, b) =>
            Math.Abs(b.SourceCount - b.TargetCount).CompareTo(Math.Abs(a.SourceCount - a.TargetCount)));

        return new TableDataDiffResult(
            tableName,
            SourceRowCount: sourceCounts.Values.Sum(),
            TargetRowCount: targetCounts.Values.Sum(),
            MatchedRowCount: matched,
            Discrepancies: discrepancies);
    }
}
