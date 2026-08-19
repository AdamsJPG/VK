using DataCompare.Engine.DataComparison;

namespace DataCompare.Engine.Tests.DataComparison
{

    /// <summary>
    /// Verifies the multiset row-hash diffing logic in DataCompare.Engine.DataComparison.TableHashComparer,
    /// including duplicate-row handling and discrepancy ordering.
    /// </summary>
    public sealed class TableHashComparerTests
    {
        [Fact]
        public void Diff_IdenticalCounts_ReportsNoDiscrepanciesAndAllMatched()
        {
            var source = new Dictionary<string, long> { ["h1"] = 3, ["h2"] = 1 };
            var target = new Dictionary<string, long> { ["h1"] = 3, ["h2"] = 1 };

            var result = new TableHashComparer().Diff("dbo.T", source, target);

            Assert.True(result.IsIdentical);
            Assert.Equal(4, result.MatchedRowCount);
            Assert.Empty(result.Discrepancies);
        }

        [Fact]
        public void Diff_HashOnlyInSource_IsReportedAsMissingFromTarget()
        {
            var source = new Dictionary<string, long> { ["h1"] = 2 };
            var target = new Dictionary<string, long>();

            var result = new TableHashComparer().Diff("dbo.T", source, target);

            Assert.False(result.IsIdentical);
            var discrepancy = Assert.Single(result.Discrepancies);
            Assert.Equal(2, discrepancy.SourceCount);
            Assert.Equal(0, discrepancy.TargetCount);
            Assert.Equal(2, discrepancy.MissingFromTarget);
            Assert.Equal(0, discrepancy.MissingFromSource);
            Assert.Equal(2, result.RowsMissingFromTarget);
            Assert.Equal(0, result.MatchedRowCount);
        }

        [Fact]
        public void Diff_HashOnlyInTarget_IsReportedAsMissingFromSource()
        {
            var source = new Dictionary<string, long>();
            var target = new Dictionary<string, long> { ["h1"] = 5 };

            var result = new TableHashComparer().Diff("dbo.T", source, target);

            var discrepancy = Assert.Single(result.Discrepancies);
            Assert.Equal(5, discrepancy.MissingFromSource);
            Assert.Equal(5, result.RowsMissingFromSource);
        }

        [Fact]
        public void Diff_PartialDuplicateMismatch_MatchesMinimumCountAndReportsRemainder()
        {
            // 3 identical rows in source, 2 in target: 2 matched as duplicates, 1 reported missing —
            // this is the multiset semantics that makes legitimate duplicate rows not a false full mismatch.
            var source = new Dictionary<string, long> { ["h1"] = 3 };
            var target = new Dictionary<string, long> { ["h1"] = 2 };

            var result = new TableHashComparer().Diff("dbo.T", source, target);

            Assert.Equal(2, result.MatchedRowCount);
            var discrepancy = Assert.Single(result.Discrepancies);
            Assert.Equal(1, discrepancy.MissingFromTarget);
        }

        [Fact]
        public void Diff_DiscrepanciesAreSortedByLargestCountDeltaFirst()
        {
            var source = new Dictionary<string, long> { ["small"] = 1, ["big"] = 10 };
            var target = new Dictionary<string, long> { ["small"] = 0, ["big"] = 0 };

            var result = new TableHashComparer().Diff("dbo.T", source, target);

            Assert.Equal("big", result.Discrepancies[0].Hash);
            Assert.Equal("small", result.Discrepancies[1].Hash);
        }

        [Fact]
        public void Diff_RowCountsSumAcrossAllHashes()
        {
            var source = new Dictionary<string, long> { ["h1"] = 3, ["h2"] = 4 };
            var target = new Dictionary<string, long> { ["h1"] = 3 };

            var result = new TableHashComparer().Diff("dbo.T", source, target);

            Assert.Equal(7, result.SourceRowCount);
            Assert.Equal(3, result.TargetRowCount);
        }
    }
}
