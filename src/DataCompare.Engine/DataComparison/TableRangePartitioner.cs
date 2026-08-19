using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.DataComparison;

/// <summary>
/// Computes key-range boundaries for splitting one very large table's comparison across several
/// concurrent workers (planning.md §19). Samples actual key values at roughly evenly-spaced row
/// positions via indexed OFFSET/FETCH seeks — cheap even for huge tables, since the leading key
/// column is normally the clustered index — rather than dividing the key's numeric range evenly,
/// which skews badly whenever keys have gaps (deleted rows, non-uniform inserts).
/// </summary>
public sealed class TableRangePartitioner
{
    /// <summary>
    /// Computes up to <paramref name="chunkCount"/> - 1 boundary values that split the table into
    /// roughly even-sized key ranges. The boundary values are sampled from whichever connection is
    /// passed in — used only to balance chunk sizes, not for correctness, since both sides of the
    /// comparison apply the same boundaries.
    /// </summary>
    /// <param name="connection">an open Microsoft.Data.SqlClient.SqlConnection to sample from</param>
    /// <param name="table">a DataCompare.Engine.Schema.TableSchema describing the table to sample</param>
    /// <param name="leadingKeyColumn">the leading primary-key column to partition by</param>
    /// <param name="chunkCount">the desired number of chunks; fewer boundaries are returned when the table has too few rows to fill them</param>
    /// <param name="approximateRowCount">an approximate row count (e.g. from sys.partitions) used to space the sample points — doesn't need to be exact</param>
    /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the sampling</param>
    /// <returns>returns a System.Collections.Generic.IReadOnlyList of System.Object holding the boundary key values in ascending order</returns>
    public async Task<IReadOnlyList<object>> ComputeBoundariesAsync(
        SqlConnection connection,
        TableSchema table,
        string leadingKeyColumn,
        int chunkCount,
        long approximateRowCount,
        CancellationToken cancellationToken = default)
    {
        var boundaries = new List<object>();
        if (chunkCount < 2 || approximateRowCount < 2)
        {
            return boundaries;
        }

        var chunkSize = (long)Math.Ceiling(approximateRowCount / (double)chunkCount);
        var quotedColumn = SqlIdentifier.Quote(leadingKeyColumn);
        var sql = $"SELECT {quotedColumn} FROM {SqlIdentifier.QuoteTable(table)} " +
            $"ORDER BY {quotedColumn} OFFSET @Offset ROWS FETCH NEXT 1 ROWS ONLY;";

        for (var chunkIndex = 1; chunkIndex < chunkCount; chunkIndex++)
        {
            var offset = chunkIndex * chunkSize;
            if (offset >= approximateRowCount)
            {
                break;
            }

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add(new SqlParameter("@Offset", System.Data.SqlDbType.BigInt) { Value = offset });
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is not null && value is not DBNull)
            {
                boundaries.Add(value);
            }
        }

        return boundaries;
    }

    /// <summary>
    /// Turns a sorted list of boundary values into N contiguous, non-overlapping key ranges
    /// covering the whole table.
    /// </summary>
    /// <param name="columnName">the leading key column the boundaries apply to</param>
    /// <param name="boundaries">the boundary values, in ascending order, as computed by <see cref="ComputeBoundariesAsync"/></param>
    /// <returns>returns a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.DataComparison.KeyRange covering the full table, one more range than there are boundaries</returns>
    public static IReadOnlyList<KeyRange> BuildRanges(string columnName, IReadOnlyList<object> boundaries)
    {
        var ranges = new List<KeyRange>();
        object? lower = null;
        foreach (var boundary in boundaries)
        {
            ranges.Add(new KeyRange(columnName, lower, boundary));
            lower = boundary;
        }

        ranges.Add(new KeyRange(columnName, lower, null));
        return ranges;
    }
}
