using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Row-level data diff via server-side content hashing (planning.md §6): pulls (hash, count) pairs
    /// rather than full rows, then diffs the two multisets in-memory — scales to large tables because
    /// only a few dozen bytes per distinct row content travel back, not the row itself.
    /// </summary>
    public sealed class TableHashComparer
    {
        /// <summary>
        /// Reads the (hash, count) pairs for every distinct row content in a table.
        /// </summary>
        /// <param name="connection">an open Microsoft.Data.SqlClient.SqlConnection to query</param>
        /// <param name="table">a DataCompare.Engine.Schema.TableSchema describing the table to query</param>
        /// <param name="columnNames">the column names to include in the row hash</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the read</param>
        /// <returns>returns a System.Collections.Generic.Dictionary of System.String to System.Int64 mapping each distinct row hash (as a hex string) to its occurrence count</returns>
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
        /// <param name="tableName">the name of the table being compared, carried through into the result</param>
        /// <param name="sourceCounts">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Int64 mapping each source-side row hash to its occurrence count</param>
        /// <param name="targetCounts">a System.Collections.Generic.IReadOnlyDictionary of System.String to System.Int64 mapping each target-side row hash to its occurrence count</param>
        /// <returns>returns a DataCompare.Engine.DataComparison.TableDataDiffResult summarizing row counts, matches, and per-hash discrepancies</returns>
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
}
