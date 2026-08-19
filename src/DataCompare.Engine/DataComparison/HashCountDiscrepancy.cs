namespace DataCompare.Engine.DataComparison
{

    /// <summary>One row-content hash whose occurrence count differs between source and target.</summary>
    /// <param name="Hash">a System.String holding the row-content hash that occurs a different number of times in the source and target tables</param>
    /// <param name="SourceCount">a System.Int64 holding the number of times this hash occurs in the source table</param>
    /// <param name="TargetCount">a System.Int64 holding the number of times this hash occurs in the target table</param>
    public sealed record HashCountDiscrepancy(string Hash, long SourceCount, long TargetCount)
    {
        /// <summary>
        /// The number of occurrences of this hash present in the source table but not accounted for in the target table.
        /// </summary>
        /// <returns>returns a System.Int64 that is zero when the target has as many or more occurrences than the source</returns>
        public long MissingFromTarget => Math.Max(0, SourceCount - TargetCount);

        /// <summary>
        /// The number of occurrences of this hash present in the target table but not accounted for in the source table.
        /// </summary>
        /// <returns>returns a System.Int64 that is zero when the source has as many or more occurrences than the target</returns>
        public long MissingFromSource => Math.Max(0, TargetCount - SourceCount);
    }
}
