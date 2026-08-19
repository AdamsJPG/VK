using System.Data;
using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Fetches a small sample of actual rows for one mismatched hash — used purely for reporting
    /// (locating/eyeballing a discrepancy), not for the matching decision itself.
    /// </summary>
    public sealed class RowDrillDownFetcher
    {
        /// <summary>
        /// Fetches a sample of rows whose computed content hash matches the given hash value.
        /// </summary>
        /// <param name="connection">an open Microsoft.Data.SqlClient.SqlConnection to query</param>
        /// <param name="table">a DataCompare.Engine.Schema.TableSchema describing the table to query</param>
        /// <param name="columnNames">the column names included in the row hash</param>
        /// <param name="hashHex">the mismatched row hash, as a hex string, to look up sample rows for</param>
        /// <param name="sampleSize">the maximum number of sample rows to fetch</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the fetch</param>
        /// <returns>returns a System.Collections.Generic.IReadOnlyList of System.Collections.Generic.IReadOnlyDictionary of System.String to nullable System.Object, one entry per sampled row</returns>
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
}
