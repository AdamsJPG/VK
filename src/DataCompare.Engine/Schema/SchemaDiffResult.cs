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
        /// computes two difference percentages at two different levels: how much of the union of all
        /// tables exists on only one side (catches new/missing tables), and how much of the tables
        /// common to both sides have column differences.
        /// </summary>
        /// <param name="sourceTableCount">a System.Int32 holding the total number of tables in the source database</param>
        /// <returns>returns a value tuple of two System.Double percentages: TableDifferencePercent (only-in-one-side tables over the union of all tables) and SchemaDifferencePercent (tables with column differences over tables common to both sides), each zero when its denominator is zero</returns>
        public (double TableDifferencePercent, double SchemaDifferencePercent) ComputeDifferencePercentages(int sourceTableCount)
        {
            var commonTableCount = sourceTableCount - TablesOnlyInSource.Count;
            var totalTableCount = TablesOnlyInSource.Count + TablesOnlyInTarget.Count + commonTableCount;
            var tableDifferencePercent = totalTableCount == 0
                ? 0
                : (TablesOnlyInSource.Count + TablesOnlyInTarget.Count) * 100.0 / totalTableCount;
            var schemaDifferencePercent = commonTableCount == 0 ? 0 : TableDiffs.Count * 100.0 / commonTableCount;
            return (tableDifferencePercent, schemaDifferencePercent);
        }
    }
}
