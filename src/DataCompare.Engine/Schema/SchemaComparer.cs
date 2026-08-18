namespace DataCompare.Engine.Schema;

/// <summary>
/// Pure diff logic over already-read schemas — no DB access, fully unit-testable.
/// </summary>
public sealed class SchemaComparer
{
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

    private static bool ColumnsEqual(ColumnSchema a, ColumnSchema b) =>
        string.Equals(a.DataType, b.DataType, StringComparison.OrdinalIgnoreCase)
        && a.MaxLength == b.MaxLength
        && a.Precision == b.Precision
        && a.Scale == b.Scale
        && a.IsNullable == b.IsNullable
        && a.IsIdentity == b.IsIdentity
        && a.IsPrimaryKey == b.IsPrimaryKey;
}
