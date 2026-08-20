using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.Schema
{

    /// <summary>
    /// tests DataCompare.Engine.Schema.SchemaDiffResult.ComputeDifferencePercentages, the table-count-level
    /// and column-count-level difference percentages shown as a separate summary line alongside the
    /// existing "N only in source, M only in target, K with column differences" sentence.
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

            var (tableDifferencePercent, schemaDifferencePercent) = result.ComputeDifferencePercentages(sourceTableCount: 10);

            // (2 + 1) only-in-one-side tables over (2 + 1 + 8) = 11 tables total.
            Assert.Equal(3 * 100.0 / 11, tableDifferencePercent, precision: 6);
            // 3 tables with column diffs over 8 tables common to both sides.
            Assert.Equal(37.5, schemaDifferencePercent, precision: 6);
        }

        [Fact]
        public void ComputeDifferencePercentages_IdenticalSchemas_ReturnsZeroForBoth()
        {
            var result = new SchemaDiffResult([], [], []);

            var (tableDifferencePercent, schemaDifferencePercent) = result.ComputeDifferencePercentages(sourceTableCount: 10);

            Assert.Equal(0, tableDifferencePercent);
            Assert.Equal(0, schemaDifferencePercent);
        }

        [Fact]
        public void ComputeDifferencePercentages_NoTablesAtAll_AvoidsDivideByZero()
        {
            var result = new SchemaDiffResult([], [], []);

            var (tableDifferencePercent, schemaDifferencePercent) = result.ComputeDifferencePercentages(sourceTableCount: 0);

            Assert.Equal(0, tableDifferencePercent);
            Assert.Equal(0, schemaDifferencePercent);
        }
    }
}
