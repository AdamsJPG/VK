namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// The result of comparing two database schemas: tables that exist only on one side, plus the
    /// per-table column differences for tables that exist on both sides.
    /// </summary>
    /// <param name="TablesOnlyInSource">an System.Collections.Generic.IReadOnlyList of System.String table names that exist only in the source database</param>
    /// <param name="TablesOnlyInTarget">an System.Collections.Generic.IReadOnlyList of System.String table names that exist only in the target database</param>
    /// <param name="TableDiffs">an System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.TableDiff objects for tables present in both databases</param>
    public sealed record SchemaDiffResult(
        IReadOnlyList<string> TablesOnlyInSource,
        IReadOnlyList<string> TablesOnlyInTarget,
        IReadOnlyList<TableDiff> TableDiffs)
    {
        /// <summary>
        /// true when there are no table-level or column-level differences between the two schemas.
        /// </summary>
        public bool IsIdentical =>
            TablesOnlyInSource.Count == 0 && TablesOnlyInTarget.Count == 0 && TableDiffs.Count == 0;
    }
}
