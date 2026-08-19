using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Fetches the real (un-hashed) value of one large-content column for a single row, identified by
    /// its primary key — used only on demand, when a user drills into a row that <see
    /// cref="KeyedTableComparer"/> flagged as changed via a <see cref="LargeContentColumn"/> hash
    /// mismatch (planning.md §18). Scoped to varbinary columns: the only current caller opens the
    /// result as a file, which only makes sense for binary content.
    /// </summary>
    public sealed class LargeContentValueFetcher
    {
        /// <summary>
        /// Fetches one varbinary column's raw value for the row matching the given key values.
        /// </summary>
        /// <param name="connection">an open Microsoft.Data.SqlClient.SqlConnection to query</param>
        /// <param name="table">a DataCompare.Engine.Schema.TableSchema describing the table to query</param>
        /// <param name="columnName">the varbinary column to fetch</param>
        /// <param name="keyValues">the primary-key column name/value pairs identifying the row</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the fetch</param>
        /// <returns>returns a System.Byte array holding the column's raw bytes, or null if the row no
        /// longer exists or the value is NULL</returns>
        public async Task<byte[]?> FetchValueAsync(
            SqlConnection connection,
            TableSchema table,
            string columnName,
            IReadOnlyDictionary<string, object?> keyValues,
            CancellationToken cancellationToken = default)
        {
            var keyNames = keyValues.Keys.ToList();
            var whereClause = string.Join(
                " AND ", keyNames.Select((name, i) => $"{SqlIdentifier.Quote(name)} = @p{i}"));
            var sql = $"SELECT {SqlIdentifier.Quote(columnName)} FROM {SqlIdentifier.QuoteTable(table)} WHERE {whereClause};";

            await using var command = new SqlCommand(sql, connection);
            for (var i = 0; i < keyNames.Count; i++)
            {
                command.Parameters.AddWithValue($"@p{i}", keyValues[keyNames[i]] ?? DBNull.Value);
            }

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result as byte[];
        }
    }
}
