using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.Schema
{

    /// <summary>
    /// tests for the DataCompare.Engine.Schema.TableDdlDiffBuilder class, covering how source and
    /// target DDL lines are aligned and highlighted when tables and columns differ.
    /// </summary>
    public sealed class TableDdlDiffBuilderTests
    {
        /// <summary>
        /// builds a DataCompare.Engine.Schema.ColumnSchema instance for use in test data.
        /// </summary>
        /// <param name="name">a System.String containing the column name</param>
        /// <param name="dataType">a System.String containing the SQL data type</param>
        /// <param name="isNullable">a System.Boolean indicating whether the column allows nulls</param>
        /// <returns>returns a DataCompare.Engine.Schema.ColumnSchema object</returns>
        private static ColumnSchema Column(string name, string dataType = "int", bool isNullable = true) =>
            new(name, dataType, 0, 0, 0, isNullable, IsIdentity: false, IsPrimaryKey: false);

        /// <summary>
        /// builds a DataCompare.Engine.Schema.TableSchema instance for use in test data.
        /// </summary>
        /// <param name="name">a System.String containing the table name</param>
        /// <param name="columns">a DataCompare.Engine.Schema.ColumnSchema array containing the columns belonging to the table</param>
        /// <returns>returns a DataCompare.Engine.Schema.TableSchema object</returns>
        private static TableSchema Table(string name, params ColumnSchema[] columns) => new("dbo", name, columns);

        [Fact]
        public void BuildDiffLines_IdenticalTables_NoLinesHighlighted()
        {
            var table = Table("Widgets", Column("Id"), Column("Name", "nvarchar"));

            var (source, target) = TableDdlDiffBuilder.BuildDiffLines(table, table);

            Assert.DoesNotContain(source, l => l.IsHighlighted);
            Assert.DoesNotContain(target, l => l.IsHighlighted);
            Assert.Equal(source.Count, target.Count);
        }

        [Fact]
        public void BuildDiffLines_TargetMissing_HighlightsEntireSourceAndLeavesTargetEmpty()
        {
            var table = Table("Widgets", Column("Id"));

            var (source, target) = TableDdlDiffBuilder.BuildDiffLines(table, null);

            Assert.All(source, l => Assert.True(l.IsHighlighted));
            Assert.Empty(target);
        }

        [Fact]
        public void BuildDiffLines_SourceMissing_HighlightsEntireTargetAndLeavesSourceEmpty()
        {
            var table = Table("Widgets", Column("Id"));

            var (source, target) = TableDdlDiffBuilder.BuildDiffLines(null, table);

            Assert.All(target, l => Assert.True(l.IsHighlighted));
            Assert.Empty(source);
        }

        [Fact]
        public void BuildDiffLines_ColumnTypeChanged_HighlightsOnlyThatLine()
        {
            var source = Table("Widgets", Column("Id"), Column("Amount", "int"));
            var target = Table("Widgets", Column("Id"), Column("Amount", "bigint"));

            var (sourceLines, targetLines) = TableDdlDiffBuilder.BuildDiffLines(source, target);

            var sourceHighlighted = sourceLines.Where(l => l.IsHighlighted).ToList();
            var targetHighlighted = targetLines.Where(l => l.IsHighlighted).ToList();
            Assert.Single(sourceHighlighted);
            Assert.Single(targetHighlighted);
            Assert.Contains("Amount", sourceHighlighted[0].Text);
            Assert.Contains("bigint", targetHighlighted[0].Text);
        }

        [Fact]
        public void BuildDiffLines_ColumnOnlyInSource_TargetGetsBlankSpacerLine()
        {
            var source = Table("Widgets", Column("Id"), Column("Extra"));
            var target = Table("Widgets", Column("Id"));

            var (sourceLines, targetLines) = TableDdlDiffBuilder.BuildDiffLines(source, target);

            Assert.Equal(sourceLines.Count, targetLines.Count);
            var extraIndex = sourceLines.ToList().FindIndex(l => l.Text.Contains("Extra"));
            Assert.True(sourceLines[extraIndex].IsHighlighted);
            Assert.False(targetLines[extraIndex].IsPresent);
        }

        [Fact]
        public void BuildDiffLines_ColumnOnlyInTarget_SourceGetsBlankSpacerLine()
        {
            var source = Table("Widgets", Column("Id"));
            var target = Table("Widgets", Column("Id"), Column("Extra"));

            var (sourceLines, targetLines) = TableDdlDiffBuilder.BuildDiffLines(source, target);

            Assert.Equal(sourceLines.Count, targetLines.Count);
            var extraIndex = targetLines.ToList().FindIndex(l => l.Text.Contains("Extra"));
            Assert.True(targetLines[extraIndex].IsHighlighted);
            Assert.False(sourceLines[extraIndex].IsPresent);
        }
    }
}
