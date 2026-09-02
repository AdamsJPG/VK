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
        /// <returns>returns a DataCompare.Engine.Schema.SchemaDiffResult object describing the tables, views, columns, and routines that differ between the two schemas</returns>
        public SchemaDiffResult Compare(DatabaseSchema source, DatabaseSchema target)
        {
            // Views are just TableSchema instances tagged SchemaObjectKind.View — they're queried and
            // diffed exactly like tables (same columns, same DDL rendering), so they're folded into the
            // same table-like list rather than duplicating this whole method for a second type.
            var sourceTables = source.Tables.Concat(source.Views).ToList();
            var targetTables = target.Tables.Concat(target.Views).ToList();

            var sourceByName = sourceTables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
            var targetByName = targetTables.ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);

            var tablesOnlyInSource = sourceTables
                .Where(t => !targetByName.ContainsKey(t.FullName))
                .Select(t => t.FullName)
                .ToList();

            var tablesOnlyInTarget = targetTables
                .Where(t => !sourceByName.ContainsKey(t.FullName))
                .Select(t => t.FullName)
                .ToList();

            var tableDiffs = new List<TableDiff>();
            foreach (var sourceTable in sourceTables)
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

            var (routinesOnlyInSource, routinesOnlyInTarget, routineDiffs) = CompareRoutines(source.Routines, target.Routines);

            return new SchemaDiffResult(tablesOnlyInSource, tablesOnlyInTarget, tableDiffs)
            {
                RoutinesOnlyInSource = routinesOnlyInSource,
                RoutinesOnlyInTarget = routinesOnlyInTarget,
                RoutineDiffs = routineDiffs,
            };
        }

        /// <summary>
        /// compares the functions and stored procedures of the source and target schemas by their raw
        /// definition text — routines have no columns, so this is a name-presence and text-equality
        /// check rather than a column-level diff.
        /// </summary>
        /// <param name="source">an System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.RoutineSchema holding the source database's routines</param>
        /// <param name="target">an System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.RoutineSchema holding the target database's routines</param>
        /// <returns>returns a System.ValueTuple of the routine names only in source, only in target, and the DataCompare.Engine.Schema.RoutineDiff list for routines present on both sides whose definition text differs</returns>
        private static (List<string> OnlyInSource, List<string> OnlyInTarget, List<RoutineDiff> Diffs) CompareRoutines(
            IReadOnlyList<RoutineSchema> source, IReadOnlyList<RoutineSchema> target)
        {
            var sourceByName = source.ToDictionary(r => r.FullName, StringComparer.OrdinalIgnoreCase);
            var targetByName = target.ToDictionary(r => r.FullName, StringComparer.OrdinalIgnoreCase);

            var onlyInSource = source.Where(r => !targetByName.ContainsKey(r.FullName)).Select(r => r.FullName).ToList();
            var onlyInTarget = target.Where(r => !sourceByName.ContainsKey(r.FullName)).Select(r => r.FullName).ToList();

            var diffs = new List<RoutineDiff>();
            foreach (var sourceRoutine in source)
            {
                if (targetByName.TryGetValue(sourceRoutine.FullName, out var targetRoutine)
                    && !string.Equals(sourceRoutine.Definition, targetRoutine.Definition, StringComparison.Ordinal))
                {
                    diffs.Add(new RoutineDiff(sourceRoutine.FullName, sourceRoutine.Kind));
                }
            }

            return (onlyInSource, onlyInTarget, diffs);
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

            var definitionChanged = !string.Equals(source.Definition, target.Definition, StringComparison.Ordinal);
            return new TableDiff(source.FullName, columnsOnlyInSource, columnsOnlyInTarget, changedColumns, definitionChanged);
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
