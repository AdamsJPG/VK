namespace DataCompare.Engine.Reporting
{

    /// <summary>Per-table row-count summary for the Data comparison HTML report — deliberately a plain
    /// Engine-layer DTO (not the App layer's view model type) so this project stays independent of the
    /// UI. Exists for every common table, identical or not.</summary>
    /// <param name="TableName">a string containing the fully-qualified name of the table</param>
    /// <param name="SourceRowCount">a long containing the number of rows read from the source database</param>
    /// <param name="TargetRowCount">a long containing the number of rows read from the target database</param>
    /// <param name="MatchedCount">a long containing the number of rows found identical in both databases</param>
    /// <param name="ChangedCount">a long containing the number of rows present in both databases but with differing data</param>
    /// <param name="MissingFromTargetCount">a long containing the number of rows present in the source database but missing from the target database</param>
    /// <param name="MissingFromSourceCount">a long containing the number of rows present in the target database but missing from the source database</param>
    /// <param name="ReassignedKeyCount">a long containing the number of rows found on both sides with identical non-key values but a different key — the same row content reassigned to a new key, rather than a genuine delete-plus-insert</param>
    /// <param name="Detail">a nullable DataCompare.Engine.Reporting.DataComparisonDetailNode holding the row-level drill-down detail (row examples, changed columns) already shown on screen, or null for an identical table with nothing to drill into</param>
    public sealed record DataComparisonTableSummary(
        string TableName,
        long SourceRowCount,
        long TargetRowCount,
        long MatchedCount,
        long ChangedCount,
        long MissingFromTargetCount,
        long MissingFromSourceCount,
        long ReassignedKeyCount = 0,
        DataComparisonDetailNode? Detail = null)
    {
        /// <summary>
        /// gets a value indicating whether this table has any changed, added, missing, or reassigned-key rows between the source and target databases.
        /// </summary>
        public bool HasDifferences => ChangedCount > 0 || MissingFromTargetCount > 0 || MissingFromSourceCount > 0 || ReassignedKeyCount > 0;
    }
}
