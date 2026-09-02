namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// Builds a side-by-side diff of two routine/view definition texts (raw T-SQL) — used for objects
    /// whose comparison unit is a single block of SQL text rather than typed columns (functions, stored
    /// procedures, and a view's underlying query). Aligns lines via a longest-common-subsequence line
    /// match (the same technique <c>diff</c>/git use), not a naive position-by-position comparison — a
    /// single inserted or deleted line (e.g. a reformatted header, an added comment) shifts every line
    /// after it by one, and comparing purely by position would flag the entire rest of the definition
    /// as changed instead of just the one real edit.
    /// </summary>
    public static class DefinitionDiffBuilder
    {
        // The LCS table is O(sourceLines * targetLines) ints. A stored procedure/view is realistically
        // at most a few thousand lines, so this stays small in practice; this cap exists only to bound
        // worst-case memory (4000x4000 = 64MB) if something unusually huge shows up, falling back to a
        // plain positional comparison rather than trying to allocate an enormous table.
        private const int MaxLinesForAlignedDiff = 4000;

        /// <summary>
        /// Builds the side-by-side definition-text lines, aligning matching lines and highlighting the
        /// ones that were actually added or removed between source and target, or the whole definition
        /// when it only exists on one side.
        /// </summary>
        /// <param name="sourceDefinition">a nullable System.String holding the source side's definition text, or null when it doesn't exist there or SQL Server won't disclose it</param>
        /// <param name="targetDefinition">a nullable System.String holding the target side's definition text, or null when it doesn't exist there or SQL Server won't disclose it</param>
        /// <returns>returns a System.ValueTuple of two System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.DefinitionDiffLine, one per side</returns>
        public static (IReadOnlyList<DefinitionDiffLine> Source, IReadOnlyList<DefinitionDiffLine> Target) BuildDiffLines(
            string? sourceDefinition, string? targetDefinition)
        {
            if (sourceDefinition is null && targetDefinition is null)
            {
                return ([], []);
            }

            if (targetDefinition is null)
            {
                return (SplitLines(sourceDefinition!).Select(text => new DefinitionDiffLine(text, IsHighlighted: true)).ToList(), []);
            }

            if (sourceDefinition is null)
            {
                return ([], SplitLines(targetDefinition).Select(text => new DefinitionDiffLine(text, IsHighlighted: true)).ToList());
            }

            var sourceRawLines = SplitLines(sourceDefinition);
            var targetRawLines = SplitLines(targetDefinition);

            return sourceRawLines.Count * targetRawLines.Count <= MaxLinesForAlignedDiff * MaxLinesForAlignedDiff
                ? BuildAlignedDiff(sourceRawLines, targetRawLines)
                : BuildPositionalDiff(sourceRawLines, targetRawLines);
        }

        /// <summary>
        /// Aligns two line sequences by their longest common subsequence: lines in the LCS are paired
        /// unchanged on the same row; a source line with no counterpart in the LCS is a removal (shown
        /// only on the source side, with a blank spacer on the target side) and vice versa for an
        /// addition — the same shape <c>diff</c>/git produce for a line replacement (one removed line,
        /// one added line), not a single "changed" row.
        /// </summary>
        /// <param name="sourceLines">a System.Collections.Generic.List of System.String holding the source side's lines</param>
        /// <param name="targetLines">a System.Collections.Generic.List of System.String holding the target side's lines</param>
        /// <returns>returns a System.ValueTuple of two System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.DefinitionDiffLine, one per side, aligned line-for-line</returns>
        private static (IReadOnlyList<DefinitionDiffLine> Source, IReadOnlyList<DefinitionDiffLine> Target) BuildAlignedDiff(
            List<string> sourceLines, List<string> targetLines)
        {
            var n = sourceLines.Count;
            var m = targetLines.Count;

            // lcsLength[i, j] = length of the longest common subsequence of sourceLines[i..] and
            // targetLines[j..], filled bottom-up so the walk below can greedily follow it forward.
            var lcsLength = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
            {
                for (var j = m - 1; j >= 0; j--)
                {
                    lcsLength[i, j] = string.Equals(sourceLines[i], targetLines[j], StringComparison.Ordinal)
                        ? lcsLength[i + 1, j + 1] + 1
                        : Math.Max(lcsLength[i + 1, j], lcsLength[i, j + 1]);
                }
            }

            var resultSource = new List<DefinitionDiffLine>();
            var resultTarget = new List<DefinitionDiffLine>();
            var a = 0;
            var b = 0;
            while (a < n && b < m)
            {
                if (string.Equals(sourceLines[a], targetLines[b], StringComparison.Ordinal))
                {
                    resultSource.Add(new DefinitionDiffLine(sourceLines[a], IsHighlighted: false));
                    resultTarget.Add(new DefinitionDiffLine(targetLines[b], IsHighlighted: false));
                    a++;
                    b++;
                }
                else if (lcsLength[a + 1, b] >= lcsLength[a, b + 1])
                {
                    resultSource.Add(new DefinitionDiffLine(sourceLines[a], IsHighlighted: true));
                    resultTarget.Add(DefinitionDiffLine.Blank());
                    a++;
                }
                else
                {
                    resultSource.Add(DefinitionDiffLine.Blank());
                    resultTarget.Add(new DefinitionDiffLine(targetLines[b], IsHighlighted: true));
                    b++;
                }
            }

            for (; a < n; a++)
            {
                resultSource.Add(new DefinitionDiffLine(sourceLines[a], IsHighlighted: true));
                resultTarget.Add(DefinitionDiffLine.Blank());
            }

            for (; b < m; b++)
            {
                resultSource.Add(DefinitionDiffLine.Blank());
                resultTarget.Add(new DefinitionDiffLine(targetLines[b], IsHighlighted: true));
            }

            return (resultSource, resultTarget);
        }

        /// <summary>
        /// Fallback for definitions too large for <see cref="BuildAlignedDiff"/>'s O(n*m) table: compares
        /// lines purely by position, same as before this class had real alignment. Coarser (one inserted
        /// line shifts everything after it), but bounded in memory regardless of input size.
        /// </summary>
        /// <param name="sourceLines">a System.Collections.Generic.List of System.String holding the source side's lines</param>
        /// <param name="targetLines">a System.Collections.Generic.List of System.String holding the target side's lines</param>
        /// <returns>returns a System.ValueTuple of two System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.DefinitionDiffLine, one per side</returns>
        private static (IReadOnlyList<DefinitionDiffLine> Source, IReadOnlyList<DefinitionDiffLine> Target) BuildPositionalDiff(
            List<string> sourceLines, List<string> targetLines)
        {
            var lineCount = Math.Max(sourceLines.Count, targetLines.Count);

            var resultSource = new List<DefinitionDiffLine>();
            var resultTarget = new List<DefinitionDiffLine>();
            for (var i = 0; i < lineCount; i++)
            {
                var sourceText = i < sourceLines.Count ? sourceLines[i] : null;
                var targetText = i < targetLines.Count ? targetLines[i] : null;
                var isChanged = !string.Equals(sourceText, targetText, StringComparison.Ordinal);

                resultSource.Add(sourceText is null ? DefinitionDiffLine.Blank() : new DefinitionDiffLine(sourceText, isChanged));
                resultTarget.Add(targetText is null ? DefinitionDiffLine.Blank() : new DefinitionDiffLine(targetText, isChanged));
            }

            return (resultSource, resultTarget);
        }

        /// <summary>
        /// Splits a definition text into individual lines, normalizing Windows-style line endings first.
        /// </summary>
        /// <param name="text">a System.String holding the definition text to split</param>
        /// <returns>returns a System.Collections.Generic.List of System.String holding one entry per line</returns>
        private static List<string> SplitLines(string text) =>
            text.Replace("\r\n", "\n").Split('\n').ToList();
    }
}
