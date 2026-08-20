using DataCompare.Engine.DataComparison;

namespace DataCompare.Engine.Tests.DataComparison
{

    /// <summary>
    /// tests that DataCompare.Engine.DataComparison.KeyedTableDiffResult.Combine correctly merges
    /// per-partition diff results into a single aggregate result.
    /// </summary>
    public sealed class KeyedTableDiffResultCombineTests
    {
        /// <summary>
        /// builds a single-partition DataCompare.Engine.DataComparison.KeyedTableDiffResult for
        /// table "dbo.T" with the given row counts, for use as an input to Combine.
        /// </summary>
        /// <param name="sourceRows">a System.Int64 containing the number of rows read from the source partition</param>
        /// <param name="targetRows">a System.Int64 containing the number of rows read from the target partition</param>
        /// <param name="matched">a System.Int64 containing the number of rows that matched identically</param>
        /// <param name="onlyInSourceTotal">a System.Int32 containing the total count of rows found only in the source</param>
        /// <param name="onlyInTargetTotal">a System.Int32 containing the total count of rows found only in the target</param>
        /// <param name="changedTotal">a System.Int32 containing the total count of rows whose values changed</param>
        /// <param name="onlyInSourceHashCounts">an optional System.Collections.Generic.IReadOnlyDictionary of System.String to System.Int64 holding this partition's only-in-source content-hash tallies; defaults to empty</param>
        /// <param name="onlyInTargetHashCounts">an optional System.Collections.Generic.IReadOnlyDictionary of System.String to System.Int64 holding this partition's only-in-target content-hash tallies; defaults to empty</param>
        /// <returns>returns a DataCompare.Engine.DataComparison.KeyedTableDiffResult object</returns>
        private static KeyedTableDiffResult MakePart(
            long sourceRows, long targetRows, long matched, int onlyInSourceTotal, int onlyInTargetTotal, int changedTotal,
            IReadOnlyDictionary<string, long>? onlyInSourceHashCounts = null,
            IReadOnlyDictionary<string, long>? onlyInTargetHashCounts = null) =>
            new(
                "dbo.T",
                sourceRows,
                targetRows,
                matched,
                new CappedExamples<RowExample>(
                    onlyInSourceTotal > 0 ? [new RowExample(new Dictionary<string, object?> { ["Id"] = 1 })] : [], onlyInSourceTotal),
                new CappedExamples<RowExample>(
                    onlyInTargetTotal > 0 ? [new RowExample(new Dictionary<string, object?> { ["Id"] = 2 })] : [], onlyInTargetTotal),
                new CappedExamples<ChangedRowExample>(
                    changedTotal > 0
                        ? [new ChangedRowExample(
                            new Dictionary<string, object?> { ["Id"] = 3 }, ["Name"],
                            new Dictionary<string, object?> { ["Name"] = "A" }, new Dictionary<string, object?> { ["Name"] = "B" })]
                        : [],
                    changedTotal),
                onlyInSourceHashCounts ?? new Dictionary<string, long>(),
                onlyInTargetHashCounts ?? new Dictionary<string, long>(),
                new CappedExamples<ReassignedKeyRowExample>([], 0));

        [Fact]
        public void Combine_SumsCountsAcrossParts()
        {
            var parts = new[]
            {
                MakePart(sourceRows: 100, targetRows: 90, matched: 80, onlyInSourceTotal: 5, onlyInTargetTotal: 2, changedTotal: 1),
                MakePart(sourceRows: 200, targetRows: 210, matched: 195, onlyInSourceTotal: 0, onlyInTargetTotal: 3, changedTotal: 2),
            };

            var combined = KeyedTableDiffResult.Combine("dbo.T", parts, maxExamplesPerCategory: 10);

            Assert.Equal(300, combined.SourceRowCount);
            Assert.Equal(300, combined.TargetRowCount);
            Assert.Equal(275, combined.MatchedIdenticalCount);
            Assert.Equal(5, combined.RowsOnlyInSource.TotalCount);
            Assert.Equal(5, combined.RowsOnlyInTarget.TotalCount);
            Assert.Equal(3, combined.ChangedRows.TotalCount);
        }

        [Fact]
        public void Combine_ExampleListsAreCappedButTotalsStayExact()
        {
            var parts = Enumerable.Range(0, 5)
                .Select(_ => MakePart(sourceRows: 10, targetRows: 10, matched: 8, onlyInSourceTotal: 0, onlyInTargetTotal: 0, changedTotal: 1))
                .ToArray();

            var combined = KeyedTableDiffResult.Combine("dbo.T", parts, maxExamplesPerCategory: 3);

            Assert.Equal(5, combined.ChangedRows.TotalCount);
            Assert.Equal(3, combined.ChangedRows.Examples.Count);
        }

        [Fact]
        public void Combine_AllPartsIdentical_ReportsIsIdentical()
        {
            var parts = new[]
            {
                MakePart(sourceRows: 10, targetRows: 10, matched: 10, onlyInSourceTotal: 0, onlyInTargetTotal: 0, changedTotal: 0),
                MakePart(sourceRows: 20, targetRows: 20, matched: 20, onlyInSourceTotal: 0, onlyInTargetTotal: 0, changedTotal: 0),
            };

            var combined = KeyedTableDiffResult.Combine("dbo.T", parts, maxExamplesPerCategory: 10);

            Assert.True(combined.IsIdentical);
        }

        [Fact]
        public void Combine_SumsContentHashCountsAcrossParts()
        {
            var parts = new[]
            {
                MakePart(
                    sourceRows: 10, targetRows: 10, matched: 10, onlyInSourceTotal: 0, onlyInTargetTotal: 0, changedTotal: 0,
                    onlyInSourceHashCounts: new Dictionary<string, long> { ["hash-a"] = 2 }),
                MakePart(
                    sourceRows: 10, targetRows: 10, matched: 10, onlyInSourceTotal: 0, onlyInTargetTotal: 0, changedTotal: 0,
                    onlyInSourceHashCounts: new Dictionary<string, long> { ["hash-a"] = 3, ["hash-b"] = 1 }),
            };

            var combined = KeyedTableDiffResult.Combine("dbo.T", parts, maxExamplesPerCategory: 10);

            Assert.Equal(5, combined.OnlyInSourceContentHashCounts["hash-a"]);
            Assert.Equal(1, combined.OnlyInSourceContentHashCounts["hash-b"]);
        }
    }
}
