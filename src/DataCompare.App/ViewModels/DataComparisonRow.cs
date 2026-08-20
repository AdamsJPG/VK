namespace DataCompare.App.ViewModels
{

    /// <summary>
    /// One row in the Data comparison grid — per-table row counts, computed for every common table
    /// (identical or not), unlike <see cref="DiffTreeNode"/> which only exists for tables with actual
    /// differences. Immutable: built once, when that table's comparison finishes.
    /// </summary>
    /// <param name="tableName">a System.String holding the compared table's fully-qualified name</param>
    /// <param name="sourceRowCount">a System.Int64 row count read from the source side</param>
    /// <param name="targetRowCount">a System.Int64 row count read from the target side</param>
    /// <param name="matchedCount">a System.Int64 count of rows that matched identically between source and target</param>
    /// <param name="changedCount">a System.Int64 count of rows present on both sides but with at least one differing value</param>
    /// <param name="missingFromTargetCount">a System.Int64 count of rows present in source but missing from target</param>
    /// <param name="missingFromSourceCount">a System.Int64 count of rows present in target but missing from source</param>
    /// <param name="reassignedKeyCount">a System.Int64 count of rows found on both sides with identical non-key values but a different key</param>
    /// <param name="detailNode">a DataCompare.App.ViewModels.DiffTreeNode holding the full comparison detail, or null for an identical table</param>
    public sealed class DataComparisonRow(
        string tableName,
        long sourceRowCount,
        long targetRowCount,
        long matchedCount,
        long changedCount,
        long missingFromTargetCount,
        long missingFromSourceCount,
        long reassignedKeyCount,
        DiffTreeNode? detailNode)
    {
        /// <summary>the compared table's fully-qualified name.</summary>
        public string TableName { get; } = tableName;

        /// <summary>the row count read from the source side.</summary>
        public long SourceRowCount { get; } = sourceRowCount;

        /// <summary>the row count read from the target side.</summary>
        public long TargetRowCount { get; } = targetRowCount;

        /// <summary>the number of rows that matched identically between source and target.</summary>
        public long MatchedCount { get; } = matchedCount;

        /// <summary>the number of rows present on both sides but with at least one differing value.</summary>
        public long ChangedCount { get; } = changedCount;

        /// <summary>the number of rows present in source but missing from target.</summary>
        public long MissingFromTargetCount { get; } = missingFromTargetCount;

        /// <summary>the number of rows present in target but missing from source.</summary>
        public long MissingFromSourceCount { get; } = missingFromSourceCount;

        /// <summary>the number of rows found on both sides with identical non-key values but a
        /// different key — the same row content reassigned to a new key, rather than a genuine
        /// delete-plus-insert.</summary>
        public long ReassignedKeyCount { get; } = reassignedKeyCount;

        /// <summary>The full comparison detail (row examples, changed columns, etc.) for this table —
        /// null for an identical table, since there's nothing to drill into.</summary>
        public DiffTreeNode? DetailNode { get; } = detailNode;

        /// <summary>true when this table has any changed, missing-from-target, missing-from-source, or reassigned-key rows.</summary>
        public bool HasDifferences =>
            ChangedCount > 0 || MissingFromTargetCount > 0 || MissingFromSourceCount > 0 || ReassignedKeyCount > 0;

        /// <summary>Drives the grid's rollup grouping.</summary>
        public string GroupLabel => HasDifferences ? "Tables with differences" : "Identical tables";
    }
}
