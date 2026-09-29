using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.Schema
{

    /// <summary>
    /// tests DataCompare.Engine.Schema.SchemaObjectTypeSummaryBuilder, which splits the combined
    /// tables/views and functions/stored-procedures buckets on a DataCompare.Engine.Schema.SchemaDiffResult
    /// back out into one row per real object kind.
    /// </summary>
    public sealed class SchemaObjectTypeSummaryBuilderTests
    {
        /// <summary>
        /// builds a DataCompare.Engine.Schema.TableSchema instance for use in test data.
        /// </summary>
        /// <param name="name">a System.String containing the table or view name (unqualified — "dbo" is always used as the schema)</param>
        /// <param name="kind">a DataCompare.Engine.Schema.SchemaObjectKind indicating whether this is a table or a view</param>
        /// <returns>returns a DataCompare.Engine.Schema.TableSchema object</returns>
        private static TableSchema TableLike(string name, SchemaObjectKind kind) =>
            new("dbo", name, [], Kind: kind);

        /// <summary>
        /// builds a DataCompare.Engine.Schema.RoutineSchema instance for use in test data.
        /// </summary>
        /// <param name="name">a System.String containing the routine name (unqualified — "dbo" is always used as the schema)</param>
        /// <param name="kind">a DataCompare.Engine.Schema.RoutineKind indicating whether this is a function or a stored procedure</param>
        /// <returns>returns a DataCompare.Engine.Schema.RoutineSchema object</returns>
        private static RoutineSchema Routine(string name, RoutineKind kind) =>
            new("dbo", name, ModifiedAt: default, Definition: "AS SELECT 1", Kind: kind);

        [Fact]
        public void Build_MixOfKinds_SplitsEachBucketByRealKind()
        {
            // Source has 2 tables, 1 view, 1 function, 1 stored procedure. Target is missing one
            // table ("Orders") and has an extra stored procedure ("ArchiveOldRows").
            var source = new DatabaseSchema([TableLike("Customers", SchemaObjectKind.Table), TableLike("Orders", SchemaObjectKind.Table)])
            {
                Views = [TableLike("ActiveCustomers", SchemaObjectKind.View)],
                Routines = [Routine("CalculateTotal", RoutineKind.Function), Routine("GetActiveCustomers", RoutineKind.StoredProcedure)],
            };
            var target = new DatabaseSchema([TableLike("Customers", SchemaObjectKind.Table)])
            {
                Views = [TableLike("ActiveCustomers", SchemaObjectKind.View)],
                Routines =
                [
                    Routine("CalculateTotal", RoutineKind.Function),
                    Routine("GetActiveCustomers", RoutineKind.StoredProcedure),
                    Routine("ArchiveOldRows", RoutineKind.StoredProcedure),
                ],
            };

            var result = new SchemaComparer().Compare(source, target);
            var rows = SchemaObjectTypeSummaryBuilder.Build(result, source, target);

            var tables = Assert.Single(rows, r => r.TypeLabel == "Tables");
            Assert.Equal(2, tables.SourceCount);
            Assert.Equal(1, tables.TargetCount);
            Assert.Equal(1, tables.OnlyInSourceCount);
            Assert.Equal(0, tables.OnlyInTargetCount);

            var views = Assert.Single(rows, r => r.TypeLabel == "Views");
            Assert.Equal(1, views.SourceCount);
            Assert.Equal(1, views.TargetCount);

            var procedures = Assert.Single(rows, r => r.TypeLabel == "Stored Procedures");
            Assert.Equal(1, procedures.SourceCount);
            Assert.Equal(2, procedures.TargetCount);
            Assert.Equal(1, procedures.OnlyInTargetCount);
        }

        [Fact]
        public void Build_KindNeverRead_IsOmittedRatherThanShownAsZero()
        {
            var source = new DatabaseSchema([TableLike("Customers", SchemaObjectKind.Table)]);
            var target = new DatabaseSchema([TableLike("Customers", SchemaObjectKind.Table)]);

            var result = new SchemaComparer().Compare(source, target);
            var rows = SchemaObjectTypeSummaryBuilder.Build(result, source, target);

            Assert.DoesNotContain(rows, r => r.TypeLabel == "Views");
            Assert.DoesNotContain(rows, r => r.TypeLabel == "Functions");
            Assert.DoesNotContain(rows, r => r.TypeLabel == "Stored Procedures");
        }

        [Fact]
        public void Build_RemovedStoredProcedure_IsReportedWithFullDifferencePercent()
        {
            var source = new DatabaseSchema([]) { Routines = [Routine("OldCleanupJob", RoutineKind.StoredProcedure)] };
            var target = new DatabaseSchema([]) { Routines = [] };

            var result = new SchemaComparer().Compare(source, target);
            var procedures = Assert.Single(SchemaObjectTypeSummaryBuilder.Build(result, source, target), r => r.TypeLabel == "Stored Procedures");

            Assert.Equal(1, procedures.SourceCount);
            Assert.Equal(0, procedures.TargetCount);
            Assert.Equal(1, procedures.OnlyInSourceCount);
            Assert.Equal(100.0, procedures.DifferencePercent, precision: 6);
        }
    }
}
