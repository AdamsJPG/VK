namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// The per-object-kind row shown on the Summary tab and in the summary HTML export — one row per
    /// kind actually read (Tables, Views, Functions, Stored Procedures), giving the raw source/target
    /// counts behind the headline percentages in <see cref="SchemaDiffResult.ComputeDifferencePercentages"/>.
    /// </summary>
    /// <param name="TypeLabel">a System.String naming the object kind this row summarizes ("Tables", "Views", "Functions", or "Stored Procedures")</param>
    /// <param name="SourceCount">a System.Int32 holding the total number of objects of this kind in the source database</param>
    /// <param name="TargetCount">a System.Int32 holding the total number of objects of this kind in the target database</param>
    /// <param name="OnlyInSourceCount">a System.Int32 holding the number of objects of this kind that exist only in the source database</param>
    /// <param name="OnlyInTargetCount">a System.Int32 holding the number of objects of this kind that exist only in the target database</param>
    /// <param name="DifferentCount">a System.Int32 holding the number of objects of this kind that exist on both sides but differ</param>
    /// <param name="DifferencePercent">a System.Double holding the percentage of this kind's objects (across the union of both sides) that are only-in-one-side or different</param>
    public sealed record SchemaObjectTypeSummary(
        string TypeLabel,
        int SourceCount,
        int TargetCount,
        int OnlyInSourceCount,
        int OnlyInTargetCount,
        int DifferentCount,
        double DifferencePercent);
}
