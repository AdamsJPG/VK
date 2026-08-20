using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Computes a content hash over a row's non-key values, used to reconcile only-in-source/only-in-target
    /// rows that are actually the same content under a different key (see <see
    /// cref="KeyedTableDiffResult.ReconcileReassignedKeys"/>). This is purely an internal grouping key —
    /// it does not need to match <see cref="ColumnHashExpressionBuilder"/>'s server-side HASHBYTES
    /// convention or <see cref="DataComparisonOrchestrator"/>'s display formatting.
    /// </summary>
    public static class RowContentHash
    {
        // Both built from their code points, rather than typed as literal escapes, so no non-printable
        // byte sits directly in this source file. Separator (ASCII unit separator, 0x1F) can't appear
        // in a formatted value, so it can't be confused with one — unlike a printable delimiter such as
        // a comma, which a column's own value could legitimately contain. NullPrefix distinguishes a
        // NULL value from the literal string "NULL" a column could actually contain.
        private static readonly char Separator = (char)0x1F;
        private static readonly char NullPrefix = (char)0x00;

        /// <summary>
        /// computes a stable content hash over the given value columns, in the given order.
        /// </summary>
        /// <param name="values">a System.Collections.Generic.IReadOnlyDictionary of System.String to nullable System.Object holding the row's column values</param>
        /// <param name="valueColumnNames">a System.Collections.Generic.IReadOnlyList of System.String holding the non-key column names to include, in a fixed order shared by both sides</param>
        /// <returns>returns a System.String containing the hex-encoded SHA-256 hash of the formatted, ordered values</returns>
        public static string Compute(IReadOnlyDictionary<string, object?> values, IReadOnlyList<string> valueColumnNames)
        {
            var formatted = string.Join(Separator, valueColumnNames.Select(name => Format(values[name])));
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(formatted));
            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// formats a single column value for inclusion in the hash input.
        /// </summary>
        /// <param name="value">a nullable System.Object holding the value to format</param>
        /// <returns>returns a System.String containing the formatted value</returns>
        private static string Format(object? value) => value switch
        {
            null => NullPrefix + "NULL",
            byte[] bytes => Convert.ToHexString(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? NullPrefix + "NULL",
        };
    }
}
