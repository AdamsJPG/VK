using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.Schema;

public sealed class SchemaComparerTests
{
    private static ColumnSchema Column(
        string name,
        string dataType = "nvarchar",
        short maxLength = 50,
        byte precision = 0,
        byte scale = 0,
        bool isNullable = true,
        bool isIdentity = false,
        bool isPrimaryKey = false) =>
        new(name, dataType, maxLength, precision, scale, isNullable, isIdentity, isPrimaryKey);

    private static TableSchema Table(string name, params ColumnSchema[] columns) =>
        new("dbo", name, columns);

    [Fact]
    public void Compare_IdenticalSchemas_ReturnsNoDifferences()
    {
        var table = Table("Customers", Column("Id", isPrimaryKey: true), Column("Name"));
        var source = new DatabaseSchema([table]);
        var target = new DatabaseSchema([table]);

        var result = new SchemaComparer().Compare(source, target);

        Assert.True(result.IsIdentical);
    }

    [Fact]
    public void Compare_TableOnlyInSource_IsReported()
    {
        var source = new DatabaseSchema([Table("Customers", Column("Id"))]);
        var target = new DatabaseSchema([]);

        var result = new SchemaComparer().Compare(source, target);

        Assert.Contains("dbo.Customers", result.TablesOnlyInSource);
        Assert.Empty(result.TablesOnlyInTarget);
        Assert.False(result.IsIdentical);
    }

    [Fact]
    public void Compare_TableOnlyInTarget_IsReported()
    {
        var source = new DatabaseSchema([]);
        var target = new DatabaseSchema([Table("Orders", Column("Id"))]);

        var result = new SchemaComparer().Compare(source, target);

        Assert.Contains("dbo.Orders", result.TablesOnlyInTarget);
        Assert.Empty(result.TablesOnlyInSource);
    }

    [Fact]
    public void Compare_ColumnOnlyInSource_IsReportedAsTableDiff()
    {
        var source = new DatabaseSchema([Table("Customers", Column("Id"), Column("Email"))]);
        var target = new DatabaseSchema([Table("Customers", Column("Id"))]);

        var result = new SchemaComparer().Compare(source, target);

        var tableDiff = Assert.Single(result.TableDiffs);
        Assert.Equal("dbo.Customers", tableDiff.TableName);
        Assert.Contains("Email", tableDiff.ColumnsOnlyInSource);
        Assert.Empty(tableDiff.ColumnsOnlyInTarget);
    }

    [Fact]
    public void Compare_ColumnOnlyInTarget_IsReportedAsTableDiff()
    {
        var source = new DatabaseSchema([Table("Customers", Column("Id"))]);
        var target = new DatabaseSchema([Table("Customers", Column("Id"), Column("Phone"))]);

        var result = new SchemaComparer().Compare(source, target);

        var tableDiff = Assert.Single(result.TableDiffs);
        Assert.Contains("Phone", tableDiff.ColumnsOnlyInTarget);
    }

    [Fact]
    public void Compare_ColumnTypeChanged_IsReportedAsChangedColumn()
    {
        var source = new DatabaseSchema([Table("Customers", Column("Age", dataType: "int"))]);
        var target = new DatabaseSchema([Table("Customers", Column("Age", dataType: "bigint"))]);

        var result = new SchemaComparer().Compare(source, target);

        var tableDiff = Assert.Single(result.TableDiffs);
        var change = Assert.Single(tableDiff.ChangedColumns);
        Assert.Equal("Age", change.ColumnName);
        Assert.Equal("int", change.Source.DataType);
        Assert.Equal("bigint", change.Target.DataType);
    }

    [Fact]
    public void Compare_ColumnNullabilityChanged_IsReportedAsChangedColumn()
    {
        var source = new DatabaseSchema([Table("Customers", Column("Email", isNullable: true))]);
        var target = new DatabaseSchema([Table("Customers", Column("Email", isNullable: false))]);

        var result = new SchemaComparer().Compare(source, target);

        var tableDiff = Assert.Single(result.TableDiffs);
        Assert.Single(tableDiff.ChangedColumns);
    }

    [Fact]
    public void Compare_TableNameComparison_IsCaseInsensitive()
    {
        var source = new DatabaseSchema([Table("customers", Column("Id"))]);
        var target = new DatabaseSchema([Table("Customers", Column("Id"))]);

        var result = new SchemaComparer().Compare(source, target);

        Assert.True(result.IsIdentical);
    }
}
