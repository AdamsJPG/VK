using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.Schema
{

    /// <summary>
    /// tests for the DataCompare.Engine.Schema.DefinitionDiffBuilder class, covering how source and
    /// target definition-text lines are aligned and highlighted when routine/view definitions differ.
    /// </summary>
    public sealed class DefinitionDiffBuilderTests
    {
        [Fact]
        public void BuildDiffLines_IdenticalDefinitions_NoLinesHighlighted()
        {
            var definition = "CREATE PROCEDURE dbo.DoThing\nAS\nSELECT 1";

            var (source, target) = DefinitionDiffBuilder.BuildDiffLines(definition, definition);

            Assert.DoesNotContain(source, l => l.IsHighlighted);
            Assert.DoesNotContain(target, l => l.IsHighlighted);
            Assert.Equal(source.Count, target.Count);
        }

        [Fact]
        public void BuildDiffLines_TargetMissing_HighlightsEntireSourceAndLeavesTargetEmpty()
        {
            var (source, target) = DefinitionDiffBuilder.BuildDiffLines("CREATE PROCEDURE dbo.DoThing\nAS\nSELECT 1", null);

            Assert.All(source, l => Assert.True(l.IsHighlighted));
            Assert.Empty(target);
        }

        [Fact]
        public void BuildDiffLines_SourceMissing_HighlightsEntireTargetAndLeavesSourceEmpty()
        {
            var (source, target) = DefinitionDiffBuilder.BuildDiffLines(null, "CREATE PROCEDURE dbo.DoThing\nAS\nSELECT 1");

            Assert.All(target, l => Assert.True(l.IsHighlighted));
            Assert.Empty(source);
        }

        [Fact]
        public void BuildDiffLines_BothNull_ReturnsEmptyOnBothSides()
        {
            var (source, target) = DefinitionDiffBuilder.BuildDiffLines(null, null);

            Assert.Empty(source);
            Assert.Empty(target);
        }

        [Fact]
        public void BuildDiffLines_OneLineChanged_HighlightsOnlyThatLine()
        {
            var source = "CREATE PROCEDURE dbo.DoThing\nAS\nSELECT 1";
            var target = "CREATE PROCEDURE dbo.DoThing\nAS\nSELECT 2";

            var (sourceLines, targetLines) = DefinitionDiffBuilder.BuildDiffLines(source, target);

            var sourceHighlighted = sourceLines.Where(l => l.IsHighlighted).ToList();
            var targetHighlighted = targetLines.Where(l => l.IsHighlighted).ToList();
            Assert.Single(sourceHighlighted);
            Assert.Single(targetHighlighted);
            Assert.Equal("SELECT 1", sourceHighlighted[0].Text);
            Assert.Equal("SELECT 2", targetHighlighted[0].Text);
        }

        [Fact]
        public void BuildDiffLines_TargetHasExtraTrailingLine_SourceGetsBlankSpacerLine()
        {
            var source = "CREATE PROCEDURE dbo.DoThing\nAS\nSELECT 1";
            var target = "CREATE PROCEDURE dbo.DoThing\nAS\nSELECT 1\nGO";

            var (sourceLines, targetLines) = DefinitionDiffBuilder.BuildDiffLines(source, target);

            Assert.Equal(sourceLines.Count, targetLines.Count);
            Assert.False(sourceLines[^1].IsPresent);
            Assert.True(targetLines[^1].IsHighlighted);
        }

        [Fact]
        public void BuildDiffLines_NormalizesWindowsLineEndings()
        {
            var (source, target) = DefinitionDiffBuilder.BuildDiffLines("AS\r\nSELECT 1", "AS\nSELECT 1");

            Assert.DoesNotContain(source, l => l.IsHighlighted);
            Assert.DoesNotContain(target, l => l.IsHighlighted);
        }

        [Fact]
        public void BuildDiffLines_OneLineInsertedMidway_DoesNotCascadeHighlightThroughRestOfDefinition()
        {
            // A single inserted line shifts every subsequent line's position by one. A naive
            // position-by-position comparison would flag "AS", "SELECT 1", "SELECT 2", and "SELECT 3"
            // as all changed even though only the inserted comment is actually new — this is the exact
            // bug being regression-tested here.
            var source = "CREATE PROCEDURE dbo.DoThing\nAS\nSELECT 1\nSELECT 2\nSELECT 3";
            var target = "CREATE PROCEDURE dbo.DoThing\n-- new comment\nAS\nSELECT 1\nSELECT 2\nSELECT 3";

            var (sourceLines, targetLines) = DefinitionDiffBuilder.BuildDiffLines(source, target);

            Assert.DoesNotContain(sourceLines, l => l.IsHighlighted);
            var targetHighlighted = targetLines.Where(l => l.IsHighlighted).ToList();
            var highlighted = Assert.Single(targetHighlighted);
            Assert.Equal("-- new comment", highlighted.Text);
        }

        [Fact]
        public void BuildDiffLines_OneLineDeletedMidway_DoesNotCascadeHighlightThroughRestOfDefinition()
        {
            var source = "CREATE PROCEDURE dbo.DoThing\n-- old comment\nAS\nSELECT 1\nSELECT 2\nSELECT 3";
            var target = "CREATE PROCEDURE dbo.DoThing\nAS\nSELECT 1\nSELECT 2\nSELECT 3";

            var (sourceLines, targetLines) = DefinitionDiffBuilder.BuildDiffLines(source, target);

            Assert.DoesNotContain(targetLines, l => l.IsHighlighted);
            var sourceHighlighted = sourceLines.Where(l => l.IsHighlighted).ToList();
            var highlighted = Assert.Single(sourceHighlighted);
            Assert.Equal("-- old comment", highlighted.Text);
        }
    }
}
