using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.Schema;

public sealed class TableDdlGeneratorTests
{
    private static ColumnSchema Column(
        string name, string dataType, short maxLength = 0, byte precision = 0, byte scale = 0, bool isNullable = true) =>
        new(name, dataType, maxLength, precision, scale, isNullable, IsIdentity: false, IsPrimaryKey: false);

    [Fact]
    public void FormatColumnLine_NvarcharColumn_DividesMaxLengthByTwoForUnicode()
    {
        var line = TableDdlGenerator.FormatColumnLine(Column("Name", "nvarchar", maxLength: 100));

        Assert.Equal("[Name] [nvarchar](50) NULL", line);
    }

    [Fact]
    public void FormatColumnLine_VarcharColumn_UsesRawMaxLength()
    {
        var line = TableDdlGenerator.FormatColumnLine(Column("Name", "varchar", maxLength: 50, isNullable: false));

        Assert.Equal("[Name] [varchar](50) NOT NULL", line);
    }

    [Fact]
    public void FormatColumnLine_MaxLengthColumn_RendersMaxKeyword()
    {
        var line = TableDdlGenerator.FormatColumnLine(Column("Notes", "nvarchar", maxLength: -1));

        Assert.Equal("[Notes] [nvarchar](MAX) NULL", line);
    }

    [Fact]
    public void FormatColumnLine_DecimalColumn_UsesPrecisionAndScale()
    {
        var line = TableDdlGenerator.FormatColumnLine(Column("Amount", "decimal", precision: 10, scale: 2));

        Assert.Equal("[Amount] [decimal](10, 2) NULL", line);
    }

    [Fact]
    public void FormatColumnLine_SimpleTypeColumn_HasNoLengthSuffix()
    {
        var line = TableDdlGenerator.FormatColumnLine(Column("Id", "int", isNullable: false));

        Assert.Equal("[Id] [int] NOT NULL", line);
    }
}
