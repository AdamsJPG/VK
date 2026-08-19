namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// Pure diff logic over already-read schemas — no DB access, fully unit-testable.
    /// </summary>
    public sealed class SchemaComparer
    {
        /// <summary>
        /// compares the tables and columns of the source and target schemas and produces the set of differences.
        /// </summary>
        /// <param name="source">a DataCompare.Engine.Schema.DatabaseSchema object containing the source database's tables</param>
        /// <param name="target">a DataCompare.Engine.Schema.DatabaseSchema object containing the target database's tables</param>
        /// <returns>returns a DataCompare.Engine.Schema.SchemaDiffResult object describing the tables and columns that differ between the two schemas</returns>
        public SchemaDiffResult Compare(DatabaseSchema source, DatabaseSchema target)
        {
            var sourceByName = source.Tables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
            var targetByName = target.Tables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);

            var tablesOnlyInSource = source.Tables
                .Where(t => !targetByName.ContainsKey(t.FullName))
                .Select(t => t.FullName)
                .ToList();

            var tablesOnlyInTarget = target.Tables
                .Where(t => !sourceByName.ContainsKey(t.FullName))
                .Select(t => t.FullName)
                .ToList();

            var tableDiffs = new List<TableDiff>();
            foreach (var sourceTable in source.Tables)
            {
                if (!targetByName.TryGetValue(sourceTable.FullName, out var targetTable))
                {
                    continue;
                }

                var diff = CompareColumns(sourceTable, targetTable);
                if (diff.HasDifferences)
                {
                    tableDiffs.Add(diff);
                }
            }

            return new SchemaDiffResult(tablesOnlyInSource, tablesOnlyInTarget, tableDiffs);
        }

        /// <summary>
        /// compares the columns of a single table between the source and target schemas.
        /// </summary>
        /// <param name="source">a DataCompare.Engine.Schema.TableSchema object describing the table as defined in the source database</param>
        /// <param name="target">a DataCompare.Engine.Schema.TableSchema object describing the table as defined in the target database</param>
        /// <returns>returns a DataCompare.Engine.Schema.TableDiff object describing the columns that differ between the two versions of the table</returns>
        private static TableDiff CompareColumns(TableSchema source, TableSchema target)
        {
            var sourceColumns = source.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
            var targetColumns = target.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

            var columnsOnlyInSource = source.Columns
                .Where(c => !targetColumns.ContainsKey(c.Name))
                .Select(c => c.Name)
                .ToList();

            var columnsOnlyInTarget = target.Columns
                .Where(c => !sourceColumns.ContainsKey(c.Name))
                .Select(c => c.Name)
                .ToList();

            var changedColumns = new List<ColumnChange>();
            foreach (var sourceColumn in source.Columns)
            {
                if (targetColumns.TryGetValue(sourceColumn.Name, out var targetColumn)
                    && !ColumnsEqual(sourceColumn, targetColumn))
                {
                    changedColumns.Add(new ColumnChange(sourceColumn.Name, sourceColumn, targetColumn));
                }
            }

            return new TableDiff(source.FullName, columnsOnlyInSource, columnsOnlyInTarget, changedColumns);
        }

        /// <summary>
        /// determines whether two column definitions are equivalent for comparison purposes.
        /// </summary>
        /// <param name="a">a DataCompare.Engine.Schema.ColumnSchema object to compare</param>
        /// <param name="b">a DataCompare.Engine.Schema.ColumnSchema object to compare against</param>
        /// <returns>returns a bool that is true when the two columns have the same data type, length, precision, scale, nullability, identity, and primary key status</returns>
        private static bool ColumnsEqual(ColumnSchema a, ColumnSchema b) =>
            string.Equals(a.DataType, b.DataType, StringComparison.OrdinalIgnoreCase)
            && a.MaxLength == b.MaxLength
            && a.Precision == b.Precision
            && a.Scale == b.Scale
            && a.IsNullable == b.IsNullable
            && a.IsIdentity == b.IsIdentity
            && a.IsPrimaryKey == b.IsPrimaryKey;
    }
}
