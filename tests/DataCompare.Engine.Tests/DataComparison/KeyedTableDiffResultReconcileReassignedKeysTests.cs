using DataCompare.Engine.DataComparison;

namespace DataCompare.Engine.Tests.DataComparison
{

    /// <summary>
    /// Tests DataCompare.Engine.DataComparison.KeyedTableDiffResult.ReconcileReassignedKeys — the
    /// content-hash reconciliation that recognizes an only-in-source row and an only-in-target row as
    /// the same content reassigned to a new key, rather than a genuine delete-plus-insert.
    /// </summary>
    public sealed class KeyedTableDiffResultReconcileReassignedKeysTests
    {
        private static readonly IReadOnlyList<string> KeyColumns = ["Id"];
        private static readonly IReadOnlyList<string> ValueColumns = ["Text"];

        /// <summary>
        /// builds a KeyedTableDiffResult with the given only-in-source/only-in-target example rows and
        /// content-hash counts, and no matched/changed rows — the shape ReconcileReassignedKeys operates on.
        /// </summary>
        /// <param name="onlyInSourceExamples">a System.Collections.Generic.List of DataCompare.Engine.DataComparison.RowExample holding the only-in-source display examples</param>
        /// <param name="onlyInSourceTotal">a System.Int64 containing the exact only-in-source total</param>
        /// <param name="onlyInSourceHashCounts">a System.Collections.Generic.Dictionary of System.String to System.Int64 holding every only-in-source row's content-hash tally</param>
        /// <param name="onlyInTargetExamples">a System.Collections.Generic.List of DataCompare.Engine.DataComparison.RowExample holding the only-in-target display examples</param>
        /// <param name="onlyInTargetTotal">a System.Int64 containing the exact only-in-target total</param>
        /// <param name="onlyInTargetHashCounts">a System.Collections.Generic.Dictionary of System.String to System.Int64 holding every only-in-target row's content-hash tally</param>
        /// <returns>returns a DataCompare.Engine.DataComparison.KeyedTableDiffResult object</returns>
        private static KeyedTableDiffResult MakeDiff(
            List<RowExample> onlyInSourceExamples, long onlyInSourceTotal, Dictionary<string, long> onlyInSourceHashCounts,
            List<RowExample> onlyInTargetExamples, long onlyInTargetTotal, Dictionary<string, long> onlyInTargetHashCounts) =>
            new(
                "dbo.T",
                SourceRowCount: onlyInSourceTotal,
                TargetRowCount: onlyInTargetTotal,
                MatchedIdenticalCount: 0,
                new CappedExamples<RowExample>(onlyInSourceExamples, onlyInSourceTotal),
                new CappedExamples<RowExample>(onlyInTargetExamples, onlyInTargetTotal),
                new CappedExamples<ChangedRowExample>([], 0),
                onlyInSourceHashCounts,
                onlyInTargetHashCounts,
                new CappedExamples<ReassignedKeyRowExample>([], 0));

        [Fact]
        public void ReconcileReassignedKeys_SameContentDifferentKey_MovesRowToReassigned()
        {
            var sourceExample = new RowExample(new Dictionary<string, object?> { ["Id"] = 1, ["Text"] = "Row 1" });
            var targetExample = new RowExample(new Dictionary<string, object?> { ["Id"] = 6, ["Text"] = "Row 1" });
            var hash = RowContentHash.Compute(sourceExample.Values, ValueColumns);

            var diff = MakeDiff(
                [sourceExample], 1, new Dictionary<string, long> { [hash] = 1 },
                [targetExample], 1, new Dictionary<string, long> { [hash] = 1 });

            var reconciled = diff.ReconcileReassignedKeys(KeyColumns, ValueColumns, maxExamplesPerCategory: 10);

            Assert.Equal(0, reconciled.RowsOnlyInSource.TotalCount);
            Assert.Equal(0, reconciled.RowsOnlyInTarget.TotalCount);
            Assert.Equal(1, reconciled.ReassignedKeyRows.TotalCount);

            var moved = Assert.Single(reconciled.ReassignedKeyRows.Examples);
            Assert.Equal(1, moved.SourceKeyValues["Id"]);
            Assert.Equal(6, moved.TargetKeyValues["Id"]);
            Assert.Equal("Row 1", moved.Values["Text"]);
        }

        [Fact]
        public void ReconcileReassignedKeys_ReassignedKeyPlusChangedValue_StaysAsTwoOrphans()
        {
            var sourceExample = new RowExample(new Dictionary<string, object?> { ["Id"] = 1, ["Text"] = "Row 1" });
            var targetExample = new RowExample(new Dictionary<string, object?> { ["Id"] = 6, ["Text"] = "Row 1 (edited)" });
            var sourceHash = RowContentHash.Compute(sourceExample.Values, ValueColumns);
            var targetHash = RowContentHash.Compute(targetExample.Values, ValueColumns);

            var diff = MakeDiff(
                [sourceExample], 1, new Dictionary<string, long> { [sourceHash] = 1 },
                [targetExample], 1, new Dictionary<string, long> { [targetHash] = 1 });

            var reconciled = diff.ReconcileReassignedKeys(KeyColumns, ValueColumns, maxExamplesPerCategory: 10);

            // A row that changed key AND had a value edit can't be distinguished from an unrelated
            // delete-plus-insert without a key — intentionally not reconciled, stays as two orphans.
            Assert.Same(diff, reconciled);
            Assert.Equal(1, reconciled.RowsOnlyInSource.TotalCount);
            Assert.Equal(1, reconciled.RowsOnlyInTarget.TotalCount);
            Assert.Equal(0, reconciled.ReassignedKeyRows.TotalCount);
        }

        [Fact]
        public void ReconcileReassignedKeys_DuplicateContentOnBothSides_PairsByMinCount()
        {
            var sourceExamples = new List<RowExample>
            {
                new(new Dictionary<string, object?> { ["Id"] = 1, ["Text"] = "Same" }),
                new(new Dictionary<string, object?> { ["Id"] = 2, ["Text"] = "Same" }),
            };
            var targetExamples = new List<RowExample>
            {
                new(new Dictionary<string, object?> { ["Id"] = 10, ["Text"] = "Same" }),
                new(new Dictionary<string, object?> { ["Id"] = 11, ["Text"] = "Same" }),
                new(new Dictionary<string, object?> { ["Id"] = 12, ["Text"] = "Same" }),
            };
            var hash = RowContentHash.Compute(sourceExamples[0].Values, ValueColumns);

            var diff = MakeDiff(
                sourceExamples, 2, new Dictionary<string, long> { [hash] = 2 },
                targetExamples, 3, new Dictionary<string, long> { [hash] = 3 });

            var reconciled = diff.ReconcileReassignedKeys(KeyColumns, ValueColumns, maxExamplesPerCategory: 10);

            // Duplicate content can't claim a specific old-key/new-key identity — just "N rows of this
            // content moved" — mirroring the same min-count pairing TableHashComparer.Diff already uses.
            Assert.Equal(2, reconciled.ReassignedKeyRows.TotalCount);
            Assert.Equal(0, reconciled.RowsOnlyInSource.TotalCount);
            Assert.Equal(1, reconciled.RowsOnlyInTarget.TotalCount);
            Assert.Equal(12, Assert.Single(reconciled.RowsOnlyInTarget.Examples).Values["Id"]);
        }

        [Fact]
        public void ReconcileReassignedKeys_MatchExistsBeyondDisplayCap_ExactCountUpdatesButNoExampleShown()
        {
            // Simulates a table with far more orphan rows than the display cap: the hash-count
            // dictionaries (built from every orphan row) show a reassigned pair exists, but neither
            // side's capped example list happens to include either half of that pair.
            var diff = MakeDiff(
                [], 1, new Dictionary<string, long> { ["hash-beyond-cap"] = 1 },
                [], 1, new Dictionary<string, long> { ["hash-beyond-cap"] = 1 });

            var reconciled = diff.ReconcileReassignedKeys(KeyColumns, ValueColumns, maxExamplesPerCategory: 10);

            Assert.Equal(1, reconciled.ReassignedKeyRows.TotalCount);
            Assert.Empty(reconciled.ReassignedKeyRows.Examples);
            Assert.Equal(0, reconciled.RowsOnlyInSource.TotalCount);
            Assert.Equal(0, reconciled.RowsOnlyInTarget.TotalCount);
        }
    }
}
