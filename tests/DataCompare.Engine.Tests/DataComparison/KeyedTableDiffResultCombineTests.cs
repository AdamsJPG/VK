using DataCompare.Engine.DataComparison;

namespace DataCompare.Engine.Tests.DataComparison;

public sealed class KeyedTableDiffResultCombineTests
{
    private static KeyedTableDiffResult MakePart(
        long sourceRows, long targetRows, long matched, int onlyInSourceTotal, int onlyInTargetTotal, int changedTotal) =>
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
                changedTotal));

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
}
