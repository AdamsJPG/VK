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
        // Not positional constructor parameters — see the identical note on DatabaseSchema for why
        // (collection-typed positional parameters can't default to an empty collection expression).

        /// <summary>function/stored procedure names that exist only in the source database, empty unless routines were requested.</summary>
        public IReadOnlyList<string> RoutinesOnlyInSource { get; init; } = [];

        /// <summary>function/stored procedure names that exist only in the target database, empty unless routines were requested.</summary>
        public IReadOnlyList<string> RoutinesOnlyInTarget { get; init; } = [];

        /// <summary>functions/stored procedures present on both sides whose definition text differs, empty unless routines were requested.</summary>
        public IReadOnlyList<RoutineDiff> RoutineDiffs { get; init; } = [];

        /// <summary>
        /// true when there are no table-level, column-level, or routine-level differences between the two schemas.
        /// </summary>
        public bool IsIdentical =>
            TablesOnlyInSource.Count == 0 && TablesOnlyInTarget.Count == 0 && TableDiffs.Count == 0
            && RoutinesOnlyInSource.Count == 0 && RoutinesOnlyInTarget.Count == 0 && RoutineDiffs.Count == 0;

        /// <summary>
        /// computes two difference percentages at two different levels, across every object kind that
        /// was actually read (tables and views always; functions and stored procedures folded in too
        /// once their counts are included in <paramref name="sourceObjectCount"/>): how much of the
        /// union of all objects exists on only one side (catches new/missing objects), and how much of
        /// the objects common to both sides have column or definition differences.
        /// </summary>
        /// <param name="sourceObjectCount">a System.Int32 holding the total number of objects in the source database, summed across every kind that was read (e.g. tables + views + functions + stored procedures)</param>
        /// <returns>returns a value tuple of two System.Double percentages: TableDifferencePercent (only-in-one-side objects over the union of all objects) and SchemaDifferencePercent (objects with column/definition differences over objects common to both sides), each zero when its denominator is zero</returns>
        public (double TableDifferencePercent, double SchemaDifferencePercent) ComputeDifferencePercentages(int sourceObjectCount)
        {
            var onlyInSourceCount = TablesOnlyInSource.Count + RoutinesOnlyInSource.Count;
            var onlyInTargetCount = TablesOnlyInTarget.Count + RoutinesOnlyInTarget.Count;
            var commonObjectCount = sourceObjectCount - onlyInSourceCount;
            var totalObjectCount = onlyInSourceCount + onlyInTargetCount + commonObjectCount;
            var tableDifferencePercent = totalObjectCount == 0
                ? 0
                : (onlyInSourceCount + onlyInTargetCount) * 100.0 / totalObjectCount;
            var schemaDifferencePercent = commonObjectCount == 0
                ? 0
                : (TableDiffs.Count + RoutineDiffs.Count) * 100.0 / commonObjectCount;
            return (tableDifferencePercent, schemaDifferencePercent);
        }
    }
}
