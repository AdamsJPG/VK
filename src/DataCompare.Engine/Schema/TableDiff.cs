namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// The column-level differences found for a single table that exists in both the source and
    /// target databases.
    /// </summary>
    /// <param name="TableName">a System.String schema-qualified name of the table being compared</param>
    /// <param name="ColumnsOnlyInSource">an System.Collections.Generic.IReadOnlyList of System.String column names that exist only in the source table</param>
    /// <param name="ColumnsOnlyInTarget">an System.Collections.Generic.IReadOnlyList of System.String column names that exist only in the target table</param>
    /// <param name="ChangedColumns">an System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.ColumnChange objects for columns that exist on both sides but differ</param>
    public sealed record TableDiff(
        string TableName,
        IReadOnlyList<string> ColumnsOnlyInSource,
        IReadOnlyList<string> ColumnsOnlyInTarget,
        IReadOnlyList<ColumnChange> ChangedColumns)
    {
        /// <summary>
        /// true when this table has any column-only-in-source, column-only-in-target, or changed-column differences.
        /// </summary>
        public bool HasDifferences =>
            ColumnsOnlyInSource.Count > 0 || ColumnsOnlyInTarget.Count > 0 || ChangedColumns.Count > 0;
    }
}
