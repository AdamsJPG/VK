using DataCompare.Engine.Schema;

namespace DataCompare.Engine.Tests.Schema
{

    /// <summary>
    /// tests for the DataCompare.Engine.Schema.SchemaComparer class, covering table and column
    /// level differences between two DataCompare.Engine.Schema.DatabaseSchema instances.
    /// </summary>
    public sealed class SchemaComparerTests
    {
        /// <summary>
        /// builds a DataCompare.Engine.Schema.ColumnSchema instance for use in test data.
        /// </summary>
        /// <param name="name">a System.String containing the column name</param>
        /// <param name="dataType">a System.String containing the SQL data type</param>
        /// <param name="maxLength">a System.Int16 containing the maximum length of the column</param>
        /// <param name="precision">a System.Byte containing the numeric precision of the column</param>
        /// <param name="scale">a System.Byte containing the numeric scale of the column</param>
        /// <param name="isNullable">a System.Boolean indicating whether the column allows nulls</param>
        /// <param name="isIdentity">a System.Boolean indicating whether the column is an identity column</param>
        /// <param name="isPrimaryKey">a System.Boolean indicating whether the column is part of the primary key</param>
        /// <returns>returns a DataCompare.Engine.Schema.ColumnSchema object</returns>
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

        /// <summary>
        /// builds a DataCompare.Engine.Schema.TableSchema instance for use in test data.
        /// </summary>
        /// <param name="name">a System.String containing the table name</param>
        /// <param name="columns">a DataCompare.Engine.Schema.ColumnSchema array containing the columns belonging to the table</param>
        /// <returns>returns a DataCompare.Engine.Schema.TableSchema object</returns>
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

        /// <summary>
        /// builds a DataCompare.Engine.Schema.TableSchema instance representing a view for use in test data.
        /// </summary>
        /// <param name="name">a System.String containing the view name</param>
        /// <param name="definition">a nullable System.String containing the view's T-SQL definition text</param>
        /// <param name="columns">a DataCompare.Engine.Schema.ColumnSchema array containing the columns belonging to the view</param>
        /// <returns>returns a DataCompare.Engine.Schema.TableSchema object with Kind set to View</returns>
        private static TableSchema View(string name, string? definition, params ColumnSchema[] columns) =>
            new("dbo", name, columns, Kind: SchemaObjectKind.View, Definition: definition);

        [Fact]
        public void Compare_ViewsWithSameColumnsAndDefinition_AreIdentical()
        {
            var view = View("ActiveCustomers", "SELECT * FROM Customers WHERE Active = 1", Column("Id", isPrimaryKey: true));
            var source = new DatabaseSchema([]) { Views = [view] };
            var target = new DatabaseSchema([]) { Views = [view] };

            var result = new SchemaComparer().Compare(source, target);

            Assert.True(result.IsIdentical);
        }

        [Fact]
        public void Compare_ViewOnlyInSource_IsReportedAlongsideTables()
        {
            var source = new DatabaseSchema([]) { Views = [View("ActiveCustomers", "SELECT 1", Column("Id"))] };
            var target = new DatabaseSchema([]) { Views = [] };

            var result = new SchemaComparer().Compare(source, target);

            Assert.Contains("dbo.ActiveCustomers", result.TablesOnlyInSource);
        }

        [Fact]
        public void Compare_ViewDefinitionChangedWithSameColumns_IsReportedAsTableDiff()
        {
            var source = new DatabaseSchema([]) { Views = [View("ActiveCustomers", "SELECT * FROM Customers WHERE Active = 1", Column("Id"))] };
            var target = new DatabaseSchema([]) { Views = [View("ActiveCustomers", "SELECT * FROM Customers WHERE Active = 0", Column("Id"))] };

            var result = new SchemaComparer().Compare(source, target);

            var diff = Assert.Single(result.TableDiffs);
            Assert.True(diff.DefinitionChanged);
            Assert.Empty(diff.ColumnsOnlyInSource);
            Assert.Empty(diff.ColumnsOnlyInTarget);
            Assert.Empty(diff.ChangedColumns);
            Assert.True(diff.HasDifferences);
        }

        /// <summary>
        /// builds a DataCompare.Engine.Schema.RoutineSchema instance for use in test data.
        /// </summary>
        /// <param name="name">a System.String containing the routine name</param>
        /// <param name="definition">a nullable System.String containing the routine's T-SQL definition text</param>
        /// <param name="kind">a DataCompare.Engine.Schema.RoutineKind indicating whether this is a function or a stored procedure</param>
        /// <returns>returns a DataCompare.Engine.Schema.RoutineSchema object</returns>
        private static RoutineSchema Routine(string name, string? definition, RoutineKind kind = RoutineKind.StoredProcedure) =>
            new("dbo", name, ModifiedAt: default, Definition: definition, Kind: kind);

        [Fact]
        public void Compare_RoutinesWithSameDefinition_AreIdentical()
        {
            var routine = Routine("GetActiveCustomers", "CREATE PROCEDURE dbo.GetActiveCustomers AS SELECT 1");
            var source = new DatabaseSchema([]) { Routines = [routine] };
            var target = new DatabaseSchema([]) { Routines = [routine] };

            var result = new SchemaComparer().Compare(source, target);

            Assert.True(result.IsIdentical);
            Assert.Empty(result.RoutineDiffs);
        }

        [Fact]
        public void Compare_RoutineOnlyInSource_IsReported()
        {
            var source = new DatabaseSchema([]) { Routines = [Routine("GetActiveCustomers", "AS SELECT 1")] };
            var target = new DatabaseSchema([]) { Routines = [] };

            var result = new SchemaComparer().Compare(source, target);

            Assert.Contains("dbo.GetActiveCustomers", result.RoutinesOnlyInSource);
            Assert.Empty(result.RoutinesOnlyInTarget);
            Assert.False(result.IsIdentical);
        }

        [Fact]
        public void Compare_RoutineOnlyInTarget_IsReported()
        {
            var source = new DatabaseSchema([]) { Routines = [] };
            var target = new DatabaseSchema([]) { Routines = [Routine("GetActiveCustomers", "AS SELECT 1")] };

            var result = new SchemaComparer().Compare(source, target);

            Assert.Contains("dbo.GetActiveCustomers", result.RoutinesOnlyInTarget);
            Assert.Empty(result.RoutinesOnlyInSource);
        }

        [Fact]
        public void Compare_RoutineDefinitionChanged_IsReportedAsRoutineDiff()
        {
            var source = new DatabaseSchema([]) { Routines = [Routine("GetActiveCustomers", "AS SELECT 1", RoutineKind.Function)] };
            var target = new DatabaseSchema([]) { Routines = [Routine("GetActiveCustomers", "AS SELECT 2", RoutineKind.Function)] };

            var result = new SchemaComparer().Compare(source, target);

            var diff = Assert.Single(result.RoutineDiffs);
            Assert.Equal("dbo.GetActiveCustomers", diff.RoutineName);
            Assert.Equal(RoutineKind.Function, diff.Kind);
        }
    }
}
