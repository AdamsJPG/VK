using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Compares two tables by streaming both sides in primary-key order and stepping through them
    /// in lockstep — the same strategy SQL Data Compare uses. This is the primary data-comparison
    /// path (see planning.md §6, revised 2026-08-18): far cheaper than server-side content hashing
    /// (<see cref="TableHashComparer"/>) because there is no per-row HASHBYTES computation for most
    /// columns (the exception being MAX-length binary/text columns — see <see
    /// cref="LargeContentColumn"/> and planning.md §18), and when the primary key is also the
    /// clustering key (the common case) no sort either, since both sides are already physically
    /// ordered by it. Requires both sides to have a usable primary key — callers should fall back to
    /// <see cref="TableHashComparer"/> for keyless tables. An optional <see cref="KeyRange"/> lets a
    /// caller restrict one call to a slice of the table, so a single huge table's comparison can be
    /// split across several concurrent calls instead of running as one long single-threaded pass
    /// (planning.md §19) — see <see cref="TableRangePartitioner"/> and <see
    /// cref="KeyedTableDiffResult.Combine"/>.
    /// </summary>
    public sealed class KeyedTableComparer
    {
        /// <summary>
        /// Streams both tables in key order and diffs them without loading either side fully into memory.
        /// </summary>
        /// <param name="sourceConnection">an open Microsoft.Data.SqlClient.SqlConnection to the source database</param>
        /// <param name="targetConnection">an open Microsoft.Data.SqlClient.SqlConnection to the target database</param>
        /// <param name="sourceTable">a DataCompare.Engine.Schema.TableSchema describing the source side of the table</param>
        /// <param name="targetTable">a DataCompare.Engine.Schema.TableSchema describing the target side of the table</param>
        /// <param name="keyColumnNames">the common primary-key column names, in key order, used to align rows across both sides</param>
        /// <param name="valueColumnNames">the common non-key column names compared for equality once two rows are aligned by key</param>
        /// <param name="maxExamplesPerCategory">the maximum number of example rows retained per category for display; exact totals are still tracked beyond this cap</param>
        /// <param name="range">an optional DataCompare.Engine.DataComparison.KeyRange restricting the comparison to one slice of the table's leading key column — used to split a very large table across several concurrent calls (planning.md §19); null compares the whole table</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the streaming comparison</param>
        /// <returns>returns a DataCompare.Engine.DataComparison.KeyedTableDiffResult object summarizing matches, mismatches, and captured examples</returns>
        public async Task<KeyedTableDiffResult> CompareAsync(
            SqlConnection sourceConnection,
            SqlConnection targetConnection,
            TableSchema sourceTable,
            TableSchema targetTable,
            IReadOnlyList<string> keyColumnNames,
            IReadOnlyList<string> valueColumnNames,
            int maxExamplesPerCategory,
            KeyRange? range = null,
            CancellationToken cancellationToken = default)
        {
            var sourceSql = BuildOrderedSelect(sourceTable, keyColumnNames, valueColumnNames, range);
            var targetSql = BuildOrderedSelect(targetTable, keyColumnNames, valueColumnNames, range);

            await using var sourceCommand = new SqlCommand(sourceSql, sourceConnection);
            await using var targetCommand = new SqlCommand(targetSql, targetConnection);
            AddRangeParameters(sourceCommand, range);
            AddRangeParameters(targetCommand, range);
            await using var sourceReader = await sourceCommand.ExecuteReaderAsync(cancellationToken);
            await using var targetReader = await targetCommand.ExecuteReaderAsync(cancellationToken);

            long sourceRowCount = 0;
            long targetRowCount = 0;
            long matchedIdenticalCount = 0;
            long onlyInSourceTotal = 0;
            long onlyInTargetTotal = 0;
            long changedRowsTotal = 0;
            var onlyInSource = new List<RowExample>();
            var onlyInTarget = new List<RowExample>();
            var changedRows = new List<ChangedRowExample>();

            var hasSource = await sourceReader.ReadAsync(cancellationToken);
            var hasTarget = await targetReader.ReadAsync(cancellationToken);

            while (hasSource && hasTarget)
            {
                var keyComparison = CompareKeys(sourceReader, targetReader, keyColumnNames.Count);
                if (keyComparison < 0)
                {
                    sourceRowCount++;
                    onlyInSourceTotal++;
                    AddIfUnderCap(onlyInSource, maxExamplesPerCategory, new RowExample(ReadRow(sourceReader, keyColumnNames, valueColumnNames)));
                    hasSource = await sourceReader.ReadAsync(cancellationToken);
                }
                else if (keyComparison > 0)
                {
                    targetRowCount++;
                    onlyInTargetTotal++;
                    AddIfUnderCap(onlyInTarget, maxExamplesPerCategory, new RowExample(ReadRow(targetReader, keyColumnNames, valueColumnNames)));
                    hasTarget = await targetReader.ReadAsync(cancellationToken);
                }
                else
                {
                    sourceRowCount++;
                    targetRowCount++;
                    var changedColumns = FindChangedColumns(sourceReader, targetReader, keyColumnNames.Count, valueColumnNames);
                    if (changedColumns.Count == 0)
                    {
                        matchedIdenticalCount++;
                    }
                    else
                    {
                        changedRowsTotal++;
                        AddIfUnderCap(changedRows, maxExamplesPerCategory, new ChangedRowExample(
                            ReadRow(sourceReader, keyColumnNames, []),
                            changedColumns,
                            ReadRow(sourceReader, keyColumnNames, valueColumnNames),
                            ReadRow(targetReader, keyColumnNames, valueColumnNames)));
                    }

                    hasSource = await sourceReader.ReadAsync(cancellationToken);
                    
                    hasTarget = await targetReader.ReadAsync(cancellationToken);
                }
            }

            while (hasSource)
            {
                sourceRowCount++;
                onlyInSourceTotal++;
                AddIfUnderCap(onlyInSource, maxExamplesPerCategory, new RowExample(ReadRow(sourceReader, keyColumnNames, valueColumnNames)));
                hasSource = await sourceReader.ReadAsync(cancellationToken);
            }

            while (hasTarget)
            {
                targetRowCount++;
                onlyInTargetTotal++;
                AddIfUnderCap(onlyInTarget, maxExamplesPerCategory, new RowExample(ReadRow(targetReader, keyColumnNames, valueColumnNames)));
                hasTarget = await targetReader.ReadAsync(cancellationToken);
            }

            return new KeyedTableDiffResult(
                sourceTable.FullName,
                sourceRowCount,
                targetRowCount,
                matchedIdenticalCount,
                new CappedExamples<RowExample>(onlyInSource, onlyInSourceTotal),
                new CappedExamples<RowExample>(onlyInTarget, onlyInTargetTotal),
                new CappedExamples<ChangedRowExample>(changedRows, changedRowsTotal));
        }

        /// <summary>
        /// Builds the ordered SELECT for one side — key columns first, then value columns, sorted by
        /// the key columns so both sides can be walked in lockstep.
        /// </summary>
        /// <param name="table">a DataCompare.Engine.Schema.TableSchema describing the table to select from</param>
        /// <param name="keyColumnNames">the key column names to select and sort by, in key order</param>
        /// <param name="valueColumnNames">the non-key column names to select for value comparison</param>
        /// <param name="range">an optional DataCompare.Engine.DataComparison.KeyRange restricting the SELECT to one slice of the leading key column</param>
        /// <returns>returns a System.String containing the generated T-SQL SELECT statement</returns>
        private static string BuildOrderedSelect(
            TableSchema table, IReadOnlyList<string> keyColumnNames, IReadOnlyList<string> valueColumnNames, KeyRange? range)
        {
            var columnsByName = table.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var keySelectColumns = keyColumnNames.Select(SqlIdentifier.Quote);
            var valueSelectColumns = valueColumnNames.Select(name => BuildValueSelectExpression(columnsByName[name]));
            var orderColumns = keyColumnNames.Select(SqlIdentifier.Quote);
            var whereClause = BuildRangeWhereClause(range);
            return $"SELECT {string.Join(", ", keySelectColumns.Concat(valueSelectColumns))} " +
                $"FROM {SqlIdentifier.QuoteTable(table)} {whereClause} ORDER BY {string.Join(", ", orderColumns)};";
        }

        /// <summary>
        /// Builds the optional WHERE clause restricting a SELECT to one key range.
        /// </summary>
        /// <param name="range">an optional DataCompare.Engine.DataComparison.KeyRange to restrict by; null produces no WHERE clause</param>
        /// <returns>returns a System.String containing the WHERE clause (or an empty string when <paramref name="range"/> is null)</returns>
        private static string BuildRangeWhereClause(KeyRange? range)
        {
            if (range is null)
            {
                return string.Empty;
            }

            var quotedColumn = SqlIdentifier.Quote(range.ColumnName);
            var conditions = new List<string>();
            if (range.LowerExclusive is not null)
            {
                conditions.Add($"{quotedColumn} > @RangeLower");
            }

            if (range.UpperInclusive is not null)
            {
                conditions.Add($"{quotedColumn} <= @RangeUpper");
            }

            return conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : string.Empty;
        }

        /// <summary>
        /// Adds the parameter values referenced by <see cref="BuildRangeWhereClause"/> to a command,
        /// when a range is in use.
        /// </summary>
        /// <param name="command">a Microsoft.Data.SqlClient.SqlCommand whose SQL text was built with a range restriction</param>
        /// <param name="range">an optional DataCompare.Engine.DataComparison.KeyRange to add parameters for; null adds nothing</param>
        /// <returns>returns nothing; this is a System.Void method</returns>
        private static void AddRangeParameters(SqlCommand command, KeyRange? range)
        {
            if (range is null)
            {
                return;
            }

            if (range.LowerExclusive is not null)
            {
                command.Parameters.AddWithValue("@RangeLower", range.LowerExclusive);
            }

            if (range.UpperInclusive is not null)
            {
                command.Parameters.AddWithValue("@RangeUpper", range.UpperInclusive);
            }
        }

        /// <summary>
        /// Builds the SELECT expression for one non-key column: MAX-length binary/text columns (and
        /// <c>xml</c>) are reduced to a small server-side hash instead of being selected in full (see
        /// <see cref="LargeContentColumn"/>), so a wide report/blob/XML column doesn't have to travel
        /// over the wire twice just to detect whether it changed. Everything else is selected
        /// directly. Never applied to key columns — a hash can't be used to order or align rows by
        /// key.
        /// </summary>
        /// <param name="column">a DataCompare.Engine.Schema.ColumnSchema describing the value column to select</param>
        /// <returns>returns a System.String containing the SQL expression to select for this column</returns>
        private static string BuildValueSelectExpression(ColumnSchema column)
        {
            var quotedName = SqlIdentifier.Quote(column.Name);
            if (!LargeContentColumn.Is(column))
            {
                return quotedName;
            }

            // HASHBYTES doesn't accept xml directly — it has to be converted to text first, same as
            // ColumnHashExpressionBuilder already does for other types HASHBYTES can't hash as-is.
            var hashInput = string.Equals(column.DataType, "xml", StringComparison.OrdinalIgnoreCase)
                ? $"CONVERT(nvarchar(max), {quotedName})"
                : quotedName;
            return $"HASHBYTES('SHA2_256', {hashInput})";
        }

        /// <summary>
        /// Compares the key column values of the current row on each reader, assuming both readers
        /// select the same key columns in the same order (see <see cref="BuildOrderedSelect"/>).
        /// </summary>
        /// <param name="sourceReader">a Microsoft.Data.SqlClient.SqlDataReader positioned at the current source row</param>
        /// <param name="targetReader">a Microsoft.Data.SqlClient.SqlDataReader positioned at the current target row</param>
        /// <param name="keyColumnCount">the number of leading columns that make up the key</param>
        /// <returns>returns a System.Int32 that is negative if the source key sorts first, positive if the target key sorts first, or zero if they match</returns>
        private static int CompareKeys(SqlDataReader sourceReader, SqlDataReader targetReader, int keyColumnCount)
        {
            for (var i = 0; i < keyColumnCount; i++)
            {
                var comparison = CompareValues(sourceReader.GetValue(i), targetReader.GetValue(i));
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        /// <summary>
        /// Compares two scalar column values for ordering purposes. Assumes matching .NET types
        /// (true whenever both sides share the same column definition, the expected case); falls
        /// back to an ordinal string comparison otherwise as a defensive last resort.
        /// </summary>
        /// <param name="sourceValue">a System.Object holding the source column value</param>
        /// <param name="targetValue">a System.Object holding the target column value</param>
        /// <returns>returns a System.Int32 following System.IComparable.CompareTo ordering conventions</returns>
        private static int CompareValues(object sourceValue, object targetValue)
        {
            if (sourceValue is IComparable comparable && sourceValue.GetType() == targetValue.GetType())
            {
                return comparable.CompareTo(targetValue);
            }

            return string.CompareOrdinal(Convert.ToString(sourceValue), Convert.ToString(targetValue));
        }

        /// <summary>
        /// Finds which value columns differ between the current row on each reader, given rows
        /// already known to share the same key.
        /// </summary>
        /// <param name="sourceReader">a Microsoft.Data.SqlClient.SqlDataReader positioned at the current source row</param>
        /// <param name="targetReader">a Microsoft.Data.SqlClient.SqlDataReader positioned at the current target row</param>
        /// <param name="keyColumnCount">the number of leading columns that make up the key, used to offset into the value columns</param>
        /// <param name="valueColumnNames">the non-key column names to compare</param>
        /// <returns>returns a System.Collections.Generic.List of System.String containing the names of columns whose values differ</returns>
        private static List<string> FindChangedColumns(
            SqlDataReader sourceReader, SqlDataReader targetReader, int keyColumnCount, IReadOnlyList<string> valueColumnNames)
        {
            var changed = new List<string>();
            for (var i = 0; i < valueColumnNames.Count; i++)
            {
                var ordinal = keyColumnCount + i;
                var sourceValue = sourceReader.IsDBNull(ordinal) ? null : sourceReader.GetValue(ordinal);
                var targetValue = targetReader.IsDBNull(ordinal) ? null : targetReader.GetValue(ordinal);
                if (!ValuesEqual(sourceValue, targetValue))
                {
                    changed.Add(valueColumnNames[i]);
                }
            }

            return changed;
        }

        /// <summary>
        /// Compares two nullable column values for equality, handling byte arrays (which .NET's
        /// default Equals treats as reference equality) as a special case.
        /// </summary>
        /// <param name="sourceValue">a nullable System.Object holding the source column value</param>
        /// <param name="targetValue">a nullable System.Object holding the target column value</param>
        /// <returns>returns a System.Boolean that is true when the two values are considered equal</returns>
        private static bool ValuesEqual(object? sourceValue, object? targetValue)
        {
            if (sourceValue is null && targetValue is null)
            {
                return true;
            }

            if (sourceValue is null || targetValue is null)
            {
                return false;
            }

            if (sourceValue is byte[] sourceBytes && targetValue is byte[] targetBytes)
            {
                return sourceBytes.AsSpan().SequenceEqual(targetBytes);
            }

            return sourceValue.Equals(targetValue);
        }

        /// <summary>
        /// Reads the current row's key and value columns into a name/value dictionary for display.
        /// </summary>
        /// <param name="reader">a Microsoft.Data.SqlClient.SqlDataReader positioned at the row to read</param>
        /// <param name="keyColumnNames">the key column names, matching the leading columns selected by <see cref="BuildOrderedSelect"/></param>
        /// <param name="valueColumnNames">the value column names, matching the trailing columns selected by <see cref="BuildOrderedSelect"/></param>
        /// <returns>returns a System.Collections.Generic.IReadOnlyDictionary of System.String to nullable System.Object with one entry per selected column</returns>
        private static IReadOnlyDictionary<string, object?> ReadRow(
            SqlDataReader reader, IReadOnlyList<string> keyColumnNames, IReadOnlyList<string> valueColumnNames)
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < keyColumnNames.Count; i++)
            {
                row[keyColumnNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            for (var i = 0; i < valueColumnNames.Count; i++)
            {
                var ordinal = keyColumnNames.Count + i;
                row[valueColumnNames[i]] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            }

            return row;
        }

        /// <summary>
        /// Appends an example to the list only while it is under the display cap — the running
        /// totals in <see cref="CompareAsync"/> stay exact regardless of this cap.
        /// </summary>
        /// <param name="examples">a System.Collections.Generic.List of T to append to</param>
        /// <param name="maxExamplesPerCategory">the maximum number of examples to retain</param>
        /// <param name="example">a T value to append when under the cap</param>
        /// <returns>returns nothing; this is a System.Void method</returns>
        private static void AddIfUnderCap<T>(List<T> examples, int maxExamplesPerCategory, T example)
        {
            if (examples.Count < maxExamplesPerCategory)
            {
                examples.Add(example);
            }
        }
    }
}
