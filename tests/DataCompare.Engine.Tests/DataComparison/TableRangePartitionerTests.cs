using DataCompare.Engine.DataComparison;

namespace DataCompare.Engine.Tests.DataComparison
{

    /// <summary>
    /// Verifies the range-boundary computation and range-building logic in
    /// DataCompare.Engine.DataComparison.TableRangePartitioner.
    /// </summary>
    public sealed class TableRangePartitionerTests
    {
        [Fact]
        public void BuildRanges_NoBoundaries_ReturnsSingleOpenRange()
        {
            var ranges = TableRangePartitioner.BuildRanges("Id", []);

            var range = Assert.Single(ranges);
            Assert.Equal("Id", range.ColumnName);
            Assert.Null(range.LowerExclusive);
            Assert.Null(range.UpperInclusive);
        }

        [Fact]
        public void BuildRanges_FourBoundaries_ReturnsFiveContiguousRanges()
        {
            object[] boundaries = [100, 200, 300, 400];

            var ranges = TableRangePartitioner.BuildRanges("Id", boundaries);

            Assert.Equal(5, ranges.Count);
            Assert.Null(ranges[0].LowerExclusive);
            Assert.Equal(100, ranges[0].UpperInclusive);
            Assert.Equal(100, ranges[1].LowerExclusive);
            Assert.Equal(200, ranges[1].UpperInclusive);
            Assert.Equal(200, ranges[2].LowerExclusive);
            Assert.Equal(300, ranges[2].UpperInclusive);
            Assert.Equal(300, ranges[3].LowerExclusive);
            Assert.Equal(400, ranges[3].UpperInclusive);
            Assert.Equal(400, ranges[4].LowerExclusive);
            Assert.Null(ranges[4].UpperInclusive);
        }

        [Fact]
        public async Task ComputeBoundariesAsync_ChunkCountLessThanTwo_ReturnsEmpty()
        {
            var boundaries = await new TableRangePartitioner().ComputeBoundariesAsync(
                connection: null!, table: null!, leadingKeyColumn: "Id", chunkCount: 1, approximateRowCount: 1000);

            Assert.Empty(boundaries);
        }

        [Fact]
        public async Task ComputeBoundariesAsync_TooFewRows_ReturnsEmpty()
        {
            var boundaries = await new TableRangePartitioner().ComputeBoundariesAsync(
                connection: null!, table: null!, leadingKeyColumn: "Id", chunkCount: 5, approximateRowCount: 1);

            Assert.Empty(boundaries);
        }
    }
}
