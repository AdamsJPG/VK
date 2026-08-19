using DataCompare.Engine.DataComparison;
using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.DataComparison
{

    /// <summary>
    /// tests that DataCompare.Engine.DataComparison.LargeContentColumn.Is correctly identifies
    /// max-length binary/text/xml columns that must be compared by hash rather than by value.
    /// </summary>
    public sealed class LargeContentColumnTests
    {
        [Theory]
        [InlineData("varbinary")]
        [InlineData("nvarchar")]
        [InlineData("varchar")]
        [InlineData("VARBINARY")]
        [InlineData("xml")]
        [InlineData("XML")]
        public void Is_MaxLengthBinaryOrTextColumn_ReturnsTrue(string dataType)
        {
            var column = new ColumnSchema(
                Name: "Bytes", DataType: dataType, MaxLength: -1, Precision: 0, Scale: 0,
                IsNullable: true, IsIdentity: false, IsPrimaryKey: false);

            Assert.True(LargeContentColumn.Is(column));
        }

        [Fact]
        public void Is_FixedLengthVarbinary_ReturnsFalse()
        {
            var column = new ColumnSchema(
                Name: "Hash", DataType: "varbinary", MaxLength: 32, Precision: 0, Scale: 0,
                IsNullable: true, IsIdentity: false, IsPrimaryKey: false);

            Assert.False(LargeContentColumn.Is(column));
        }

        [Fact]
        public void Is_MaxLengthScalarType_ReturnsFalse()
        {
            var column = new ColumnSchema(
                Name: "Id", DataType: "int", MaxLength: -1, Precision: 0, Scale: 0,
                IsNullable: false, IsIdentity: true, IsPrimaryKey: true, PrimaryKeyOrdinal: 1);

            Assert.False(LargeContentColumn.Is(column));
        }
    }
}
