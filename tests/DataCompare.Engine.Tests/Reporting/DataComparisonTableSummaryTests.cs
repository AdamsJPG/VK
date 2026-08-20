using DataCompare.Engine.Reporting;

namespace DataCompare.Engine.Tests.Reporting
{

    /// <summary>
    /// tests DataCompare.Engine.Reporting.DataComparisonTableSummary.PercentDiffers, the percentage of
    /// source rows that changed, are missing, or had a reassigned key.
    /// </summary>
    public sealed class DataComparisonTableSummaryTests
    {
        [Fact]
        public void PercentDiffers_MixOfDifferenceKinds_SumsAllFourOverSourceRowCount()
        {
            // 100 source rows: 3 changed, 2 missing from target, 1 missing from source, 4 reassigned —
            // (3 + 2 + 1 + 4) / 100 = 10%.
            var summary = new DataComparisonTableSummary(
                "dbo.Client", SourceRowCount: 100, TargetRowCount: 101, MatchedCount: 90,
                ChangedCount: 3, MissingFromTargetCount: 2, MissingFromSourceCount: 1, ReassignedKeyCount: 4);

            Assert.Equal(10.0, summary.PercentDiffers);
        }

        [Fact]
        public void PercentDiffers_NoSourceRows_AvoidsDivideByZero()
        {
            var summary = new DataComparisonTableSummary(
                "dbo.Empty", SourceRowCount: 0, TargetRowCount: 0, MatchedCount: 0,
                ChangedCount: 0, MissingFromTargetCount: 0, MissingFromSourceCount: 0);

            Assert.Equal(0, summary.PercentDiffers);
        }

        [Fact]
        public void PercentDiffers_IdenticalTable_IsZero()
        {
            var summary = new DataComparisonTableSummary(
                "dbo.Currency", SourceRowCount: 5, TargetRowCount: 5, MatchedCount: 5,
                ChangedCount: 0, MissingFromTargetCount: 0, MissingFromSourceCount: 0);

            Assert.Equal(0, summary.PercentDiffers);
        }
    }
}
