using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.Schema
{

    /// <summary>
    /// tests DataCompare.Engine.Schema.SchemaDiffResult.ComputeDifferencePercentages, the object-count-level
    /// and content-level difference percentages shown as a separate summary line alongside the existing
    /// "N only in source, M only in target, K with differences" sentence — folding in tables, views,
    /// functions, and stored procedures alike.
    /// </summary>
    public sealed class SchemaDiffResultTests
    {
        [Fact]
        public void ComputeDifferencePercentages_MixOfOnlyInOneSideAndColumnDiffs_ComputesBothPercentagesIndependently()
        {
            // 10 tables in source; 2 exist only in source, 1 only in target, 3 of the remaining 8
            // common tables have column differences.
            var result = new SchemaDiffResult(
                TablesOnlyInSource: ["dbo.A", "dbo.B"],
                TablesOnlyInTarget: ["dbo.C"],
                TableDiffs: [
                    new TableDiff("dbo.D", [], [], []),
                    new TableDiff("dbo.E", [], [], []),
                    new TableDiff("dbo.F", [], [], []),
                ]);

            var (tableDifferencePercent, schemaDifferencePercent) = result.ComputeDifferencePercentages(sourceObjectCount: 10);

            // (2 + 1) only-in-one-side tables over (2 + 1 + 8) = 11 tables total.
            Assert.Equal(3 * 100.0 / 11, tableDifferencePercent, precision: 6);
            // 3 tables with column diffs over 8 tables common to both sides.
            Assert.Equal(37.5, schemaDifferencePercent, precision: 6);
        }

        [Fact]
        public void ComputeDifferencePercentages_IdenticalSchemas_ReturnsZeroForBoth()
        {
            var result = new SchemaDiffResult([], [], []);

            var (tableDifferencePercent, schemaDifferencePercent) = result.ComputeDifferencePercentages(sourceObjectCount: 10);

            Assert.Equal(0, tableDifferencePercent);
            Assert.Equal(0, schemaDifferencePercent);
        }

        [Fact]
        public void ComputeDifferencePercentages_NoTablesAtAll_AvoidsDivideByZero()
        {
            var result = new SchemaDiffResult([], [], []);

            var (tableDifferencePercent, schemaDifferencePercent) = result.ComputeDifferencePercentages(sourceObjectCount: 0);

            Assert.Equal(0, tableDifferencePercent);
            Assert.Equal(0, schemaDifferencePercent);
        }

        [Fact]
        public void ComputeDifferencePercentages_RoutinesOnlyInOneSide_AreFoldedIntoBothPercentages()
        {
            // 5 source objects total (tables + routines): 1 routine only in source, 1 table only in
            // target, and of the 4 common objects, 1 routine has a definition difference.
            var result = new SchemaDiffResult(
                TablesOnlyInSource: [],
                TablesOnlyInTarget: ["dbo.Orders"],
                TableDiffs: [])
            {
                RoutinesOnlyInSource = ["dbo.GetActiveCustomers"],
                RoutineDiffs = [new RoutineDiff("dbo.CalculateTotal", RoutineKind.Function)],
            };

            var (tableDifferencePercent, schemaDifferencePercent) = result.ComputeDifferencePercentages(sourceObjectCount: 5);

            // (1 routine + 1 table) only-in-one-side over (1 + 1 + 4) = 6 objects total.
            Assert.Equal(2 * 100.0 / 6, tableDifferencePercent, precision: 6);
            // 1 routine diff over 4 objects common to both sides.
            Assert.Equal(25.0, schemaDifferencePercent, precision: 6);
        }
    }
}
