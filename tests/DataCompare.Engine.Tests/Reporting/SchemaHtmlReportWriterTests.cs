using DataCompare.Engine.Reporting;
using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.Reporting;

public sealed class SchemaHtmlReportWriterTests
{
    private static ColumnSchema Column(string name, string dataType = "int") =>
        new(name, dataType, 0, 0, 0, IsNullable: true, IsIdentity: false, IsPrimaryKey: false);

    private static TableSchema Table(string name, params ColumnSchema[] columns) => new("dbo", name, columns);

    [Fact]
    public void Generate_ProducesValidHtmlDocumentWithSourceAndTargetLabels()
    {
        var table = Table("Widgets", Column("Id"));
        var source = new DatabaseSchema([table]);
        var target = new DatabaseSchema([table]);
        var result = new SchemaComparer().Compare(source, target);

        var html = SchemaHtmlReportWriter.Generate("srv-a / AppDb", "srv-b / AppDb", result, source, target);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("srv-a / AppDb", html);
        Assert.Contains("srv-b / AppDb", html);
        Assert.Contains("</html>", html);
    }

    [Fact]
    public void Generate_TableOnlyInSource_AppearsUnderOnlyInSourceSection()
    {
        var sourceOnlyTable = Table("Orphan", Column("Id"));
        var source = new DatabaseSchema([sourceOnlyTable]);
        var target = new DatabaseSchema([]);
        var result = new SchemaComparer().Compare(source, target);

        var html = SchemaHtmlReportWriter.Generate("A", "B", result, source, target);

        Assert.Contains("Only in Source (1)", html);
        Assert.Contains("Orphan", html);
    }

    [Fact]
    public void Generate_ChangedColumn_HighlightsTheDifferingLine()
    {
        var source = new DatabaseSchema([Table("Widgets", Column("Amount", "int"))]);
        var target = new DatabaseSchema([Table("Widgets", Column("Amount", "bigint"))]);
        var result = new SchemaComparer().Compare(source, target);

        var html = SchemaHtmlReportWriter.Generate("A", "B", result, source, target);

        Assert.Contains("Different (1)", html);
        Assert.Contains("class=\"hl\"", html);
    }

    [Fact]
    public void Generate_IdenticalTables_StillIncludedWithFullDdl()
    {
        var table = Table("Widgets", Column("Id"), Column("Name", "nvarchar"));
        var source = new DatabaseSchema([table]);
        var target = new DatabaseSchema([table]);
        var result = new SchemaComparer().Compare(source, target);

        var html = SchemaHtmlReportWriter.Generate("A", "B", result, source, target);

        Assert.Contains("Identical (1)", html);
        Assert.Contains("CREATE TABLE", html);
        Assert.Contains("[Name]", html);
    }

    [Fact]
    public void Generate_EscapesHtmlSpecialCharactersInNames()
    {
        var table = Table("Weird<Name>", Column("Id"));
        var source = new DatabaseSchema([table]);
        var target = new DatabaseSchema([]);
        var result = new SchemaComparer().Compare(source, target);

        var html = SchemaHtmlReportWriter.Generate("A", "B", result, source, target);

        Assert.DoesNotContain("Weird<Name>", html);
        Assert.Contains("Weird&lt;Name&gt;", html);
    }
}
