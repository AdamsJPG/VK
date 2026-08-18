using DataCompare.Engine.DataComparison;
using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.DataComparison;

public sealed class ColumnHashExpressionBuilderTests
{
    private static ColumnSchema Column(string name, string dataType) =>
        new(name, dataType, 50, 0, 0, IsNullable: true, IsIdentity: false, IsPrimaryKey: false);

    [Fact]
    public void BuildGroupedCountQuery_IncludesHashByteAndGroupBy()
    {
        var table = new TableSchema("dbo", "Customers", [Column("Name", "nvarchar")]);

        var sql = ColumnHashExpressionBuilder.BuildGroupedCountQuery(table, ["Name"]);

        Assert.Contains("HASHBYTES('SHA2_256'", sql);
        Assert.Contains("GROUP BY RowHash", sql);
        Assert.Contains("[dbo].[Customers]", sql);
    }

    [Fact]
    public void BuildGroupedCountQuery_WrapsColumnsInNullSentinel()
    {
        var table = new TableSchema("dbo", "Customers", [Column("Name", "nvarchar")]);

        var sql = ColumnHashExpressionBuilder.BuildGroupedCountQuery(table, ["Name"]);

        Assert.Contains("ISNULL(CONVERT(nvarchar(max), [Name])", sql);
    }

    [Theory]
    [InlineData("varbinary", ", 1)")]
    [InlineData("float", ", 2)")]
    [InlineData("datetimeoffset", ", 127)")]
    [InlineData("datetime2", ", 121)")]
    [InlineData("date", ", 23)")]
    [InlineData("time", ", 114)")]
    public void BuildGroupedCountQuery_UsesExplicitConvertStylePerDataType(string dataType, string expectedStyleSuffix)
    {
        var table = new TableSchema("dbo", "T", [Column("Col", dataType)]);

        var sql = ColumnHashExpressionBuilder.BuildGroupedCountQuery(table, ["Col"]);

        Assert.Contains(expectedStyleSuffix, sql);
    }

    [Fact]
    public void BuildSampleRowsQuery_FiltersByHashParameterWithTopSampleSize()
    {
        var table = new TableSchema("dbo", "Customers", [Column("Name", "nvarchar")]);

        var sql = ColumnHashExpressionBuilder.BuildSampleRowsQuery(table, ["Name"]);

        Assert.Contains("TOP (@SampleSize)", sql);
        Assert.Contains("WHERE HASHBYTES", sql);
        Assert.Contains("= @Hash", sql);
    }

    [Fact]
    public void BuildGroupedCountQuery_EscapesClosingBracketInIdentifiers()
    {
        var table = new TableSchema("dbo", "Weird]Table", [Column("Col", "int")]);

        var sql = ColumnHashExpressionBuilder.BuildGroupedCountQuery(table, ["Col"]);

        Assert.Contains("[Weird]]Table]", sql);
    }
}
