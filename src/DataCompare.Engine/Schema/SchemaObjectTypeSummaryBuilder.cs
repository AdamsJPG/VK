namespace DataCompare.Engine.Schema
{

    /// <summary>
    /// Builds the per-object-kind breakdown (<see cref="SchemaObjectTypeSummary"/>) shown on the Summary
    /// tab and in the summary HTML export. <see cref="SchemaDiffResult"/> only tracks tables/views as one
    /// combined bucket and functions/stored procedures as another (see <see
    /// cref="SchemaDiffResult.TablesOnlyInSource"/>/<see cref="SchemaDiffResult.RoutinesOnlyInSource"/>),
    /// so splitting each bucket back out into its four real kinds means looking each name back up
    /// against the source/target schemas that produced it.
    /// </summary>
    public static class SchemaObjectTypeSummaryBuilder
    {
        /// <summary>
        /// builds one row per object kind actually present on either side (a kind with zero objects on
        /// both sides — because its checkbox was never ticked — is left out rather than shown as a row
        /// of zeroes).
        /// </summary>
        /// <param name="result">a DataCompare.Engine.Schema.SchemaDiffResult holding the schema comparison outcome</param>
        /// <param name="source">a DataCompare.Engine.Schema.DatabaseSchema describing the source database</param>
        /// <param name="target">a DataCompare.Engine.Schema.DatabaseSchema describing the target database</param>
        /// <returns>returns a System.Collections.Generic.IReadOnlyList of DataCompare.Engine.Schema.SchemaObjectTypeSummary, one per object kind actually read</returns>
        public static IReadOnlyList<SchemaObjectTypeSummary> Build(SchemaDiffResult result, DatabaseSchema source, DatabaseSchema target)
        {
            var sourceTableLikeByName = source.Tables.Concat(source.Views).ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
            var targetTableLikeByName = target.Tables.Concat(target.Views).ToDictionary(t => t.FullName, StringComparer.OrdinalIgnoreCase);
            var sourceRoutineByName = source.Routines.ToDictionary(r => r.FullName, StringComparer.OrdinalIgnoreCase);
            var targetRoutineByName = target.Routines.ToDictionary(r => r.FullName, StringComparer.OrdinalIgnoreCase);

            var rows = new List<SchemaObjectTypeSummary>
            {
                BuildTableLikeRow(
                    "Tables", SchemaObjectKind.Table, source.Tables.Count, target.Tables.Count,
                    result, sourceTableLikeByName, targetTableLikeByName),
                BuildTableLikeRow(
                    "Views", SchemaObjectKind.View, source.Views.Count, target.Views.Count,
                    result, sourceTableLikeByName, targetTableLikeByName),
                BuildRoutineRow(
                    "Functions", RoutineKind.Function,
                    source.Routines.Count(r => r.Kind == RoutineKind.Function),
                    target.Routines.Count(r => r.Kind == RoutineKind.Function),
                    result, sourceRoutineByName, targetRoutineByName),
                BuildRoutineRow(
                    "Stored Procedures", RoutineKind.StoredProcedure,
                    source.Routines.Count(r => r.Kind == RoutineKind.StoredProcedure),
                    target.Routines.Count(r => r.Kind == RoutineKind.StoredProcedure),
                    result, sourceRoutineByName, targetRoutineByName),
            };

            return rows.Where(r => r.SourceCount > 0 || r.TargetCount > 0).ToList();
        }

        /// <summary>
        /// builds one table/view breakdown row for the given kind, filtering the schema-wide
        /// only-in-source/only-in-target/different buckets down to just this kind by looking each name
        /// back up in the source (for only-in-source and different) or target (for only-in-target) schema.
        /// </summary>
        /// <param name="typeLabel">a System.String naming this row's object kind</param>
        /// <param name="kind">a DataCompare.Engine.Schema.SchemaObjectKind identifying which kind (table or view) this row summarizes</param>
        /// <param name="sourceCount">a System.Int32 holding the total number of objects of this kind in the source database</param>
        /// <param name="targetCount">a System.Int32 holding the total number of objects of this kind in the target database</param>
        /// <param name="result">a DataCompare.Engine.Schema.SchemaDiffResult holding the schema comparison outcome</param>
        /// <param name="sourceByName">a System.Collections.Generic.IReadOnlyDictionary of DataCompare.Engine.Schema.TableSchema keyed by full name, describing the source database's tables and views</param>
        /// <param name="targetByName">a System.Collections.Generic.IReadOnlyDictionary of DataCompare.Engine.Schema.TableSchema keyed by full name, describing the target database's tables and views</param>
        /// <returns>returns a DataCompare.Engine.Schema.SchemaObjectTypeSummary for this kind</returns>
        private static SchemaObjectTypeSummary BuildTableLikeRow(
            string typeLabel,
            SchemaObjectKind kind,
            int sourceCount,
            int targetCount,
            SchemaDiffResult result,
            IReadOnlyDictionary<string, TableSchema> sourceByName,
            IReadOnlyDictionary<string, TableSchema> targetByName)
        {
            var onlyInSourceCount = result.TablesOnlyInSource.Count(n => sourceByName[n].Kind == kind);
            var onlyInTargetCount = result.TablesOnlyInTarget.Count(n => targetByName[n].Kind == kind);
            var differentCount = result.TableDiffs.Count(d => sourceByName[d.TableName].Kind == kind);

            return BuildRow(typeLabel, sourceCount, targetCount, onlyInSourceCount, onlyInTargetCount, differentCount);
        }

        /// <summary>
        /// builds one function/stored-procedure breakdown row for the given kind, filtering the
        /// schema-wide only-in-source/only-in-target buckets down to just this kind by looking each name
        /// back up in the source (for only-in-source) or target (for only-in-target) schema — the
        /// "different" count needs no lookup since <see cref="RoutineDiff"/> already carries its own kind.
        /// </summary>
        /// <param name="typeLabel">a System.String naming this row's object kind</param>
        /// <param name="kind">a DataCompare.Engine.Schema.RoutineKind identifying which kind (function or stored procedure) this row summarizes</param>
        /// <param name="sourceCount">a System.Int32 holding the total number of routines of this kind in the source database</param>
        /// <param name="targetCount">a System.Int32 holding the total number of routines of this kind in the target database</param>
        /// <param name="result">a DataCompare.Engine.Schema.SchemaDiffResult holding the schema comparison outcome</param>
        /// <param name="sourceByName">a System.Collections.Generic.IReadOnlyDictionary of DataCompare.Engine.Schema.RoutineSchema keyed by full name, describing the source database's routines</param>
        /// <param name="targetByName">a System.Collections.Generic.IReadOnlyDictionary of DataCompare.Engine.Schema.RoutineSchema keyed by full name, describing the target database's routines</param>
        /// <returns>returns a DataCompare.Engine.Schema.SchemaObjectTypeSummary for this kind</returns>
        private static SchemaObjectTypeSummary BuildRoutineRow(
            string typeLabel,
            RoutineKind kind,
            int sourceCount,
            int targetCount,
            SchemaDiffResult result,
            IReadOnlyDictionary<string, RoutineSchema> sourceByName,
            IReadOnlyDictionary<string, RoutineSchema> targetByName)
        {
            var onlyInSourceCount = result.RoutinesOnlyInSource.Count(n => sourceByName[n].Kind == kind);
            var onlyInTargetCount = result.RoutinesOnlyInTarget.Count(n => targetByName[n].Kind == kind);
            var differentCount = result.RoutineDiffs.Count(d => d.Kind == kind);

            return BuildRow(typeLabel, sourceCount, targetCount, onlyInSourceCount, onlyInTargetCount, differentCount);
        }

        /// <summary>
        /// assembles one breakdown row from its raw counts, computing the combined difference percentage
        /// (only-in-one-side plus different, over the union of both sides).
        /// </summary>
        /// <param name="typeLabel">a System.String naming this row's object kind</param>
        /// <param name="sourceCount">a System.Int32 holding the total number of objects of this kind in the source database</param>
        /// <param name="targetCount">a System.Int32 holding the total number of objects of this kind in the target database</param>
        /// <param name="onlyInSourceCount">a System.Int32 holding the number of objects of this kind only in the source database</param>
        /// <param name="onlyInTargetCount">a System.Int32 holding the number of objects of this kind only in the target database</param>
        /// <param name="differentCount">a System.Int32 holding the number of objects of this kind present on both sides but different</param>
        /// <returns>returns a DataCompare.Engine.Schema.SchemaObjectTypeSummary for this kind</returns>
        private static SchemaObjectTypeSummary BuildRow(
            string typeLabel, int sourceCount, int targetCount, int onlyInSourceCount, int onlyInTargetCount, int differentCount)
        {
            var commonCount = sourceCount - onlyInSourceCount;
            var unionCount = onlyInSourceCount + onlyInTargetCount + commonCount;
            var differencePercent = unionCount == 0 ? 0 : (onlyInSourceCount + onlyInTargetCount + differentCount) * 100.0 / unionCount;

            return new SchemaObjectTypeSummary(
                typeLabel, sourceCount, targetCount, onlyInSourceCount, onlyInTargetCount, differentCount, differencePercent);
        }
    }
}
