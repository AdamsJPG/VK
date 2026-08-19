using DataCompare.Engine.DataComparison;
using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Tests.DataComparison;

/// <summary>
/// Runs the primary-key merge-join comparer against real SQL Server (LocalDB) — sets up a source
/// and target database with deliberately different rows and checks the streaming diff catches
/// them all. Requires SQL Server LocalDB.
/// </summary>
public sealed class KeyedTableComparerIntegrationTests : IAsyncLifetime
{
    private const string MasterConnectionString = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;";
    private readonly string _sourceDatabaseName = $"DataCompareTests_Src_{Guid.NewGuid():N}";
    private readonly string _targetDatabaseName = $"DataCompareTests_Tgt_{Guid.NewGuid():N}";

    private string SourceConnectionString => $@"Server=(localdb)\MSSQLLocalDB;Database={_sourceDatabaseName};Integrated Security=true;";
    private string TargetConnectionString => $@"Server=(localdb)\MSSQLLocalDB;Database={_targetDatabaseName};Integrated Security=true;";

    public async Task InitializeAsync()
    {
        await CreateDatabaseAsync(_sourceDatabaseName);
        await CreateDatabaseAsync(_targetDatabaseName);

        await using var sourceConnection = new SqlConnection(SourceConnectionString);
        await sourceConnection.OpenAsync();
        await using var createSourceTable = new SqlCommand("""
            CREATE TABLE dbo.Widgets (Id INT PRIMARY KEY, Name NVARCHAR(50) NOT NULL, Amount DECIMAL(10,2) NULL);
            INSERT INTO dbo.Widgets (Id, Name, Amount) VALUES
                (1, 'Alice', 10.50),   -- identical on both sides
                (2, 'Bob', 20.00),     -- value changes on target
                (3, 'Carol', 30.00);   -- only in source

            CREATE TABLE dbo.Documents (Id INT PRIMARY KEY, Content VARBINARY(MAX) NULL);
            INSERT INTO dbo.Documents (Id, Content) VALUES
                (1, 0x25504446),  -- identical on both sides
                (2, 0x41414141);  -- content differs on target

            CREATE TABLE dbo.XmlDocs (Id INT PRIMARY KEY, SourceXml XML NULL);
            INSERT INTO dbo.XmlDocs (Id, SourceXml) VALUES
                (1, '<Transaction amount="10"/>'),  -- identical on both sides
                (2, '<Transaction amount="20"/>');  -- content differs on target
            """, sourceConnection);
        await createSourceTable.ExecuteNonQueryAsync();

        await using var targetConnection = new SqlConnection(TargetConnectionString);
        await targetConnection.OpenAsync();
        await using var createTargetTable = new SqlCommand("""
            CREATE TABLE dbo.Widgets (Id INT PRIMARY KEY, Name NVARCHAR(50) NOT NULL, Amount DECIMAL(10,2) NULL);
            INSERT INTO dbo.Widgets (Id, Name, Amount) VALUES
                (1, 'Alice', 10.50),   -- identical on both sides
                (2, 'Bob', 99.99),     -- changed from source
                (4, 'Dave', 40.00);    -- only in target

            CREATE TABLE dbo.Documents (Id INT PRIMARY KEY, Content VARBINARY(MAX) NULL);
            INSERT INTO dbo.Documents (Id, Content) VALUES
                (1, 0x25504446),  -- identical on both sides
                (2, 0x42424242);  -- changed from source

            CREATE TABLE dbo.XmlDocs (Id INT PRIMARY KEY, SourceXml XML NULL);
            INSERT INTO dbo.XmlDocs (Id, SourceXml) VALUES
                (1, '<Transaction amount="10"/>'),  -- identical on both sides
                (2, '<Transaction amount="99"/>');  -- changed from source
            """, targetConnection);
        await createTargetTable.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await DropDatabaseAsync(_sourceDatabaseName);
        await DropDatabaseAsync(_targetDatabaseName);
    }

    [Fact]
    public async Task CompareAsync_AgainstRealSqlServer_FindsMatchesMissingAndChangedRows()
    {
        await using var sourceConnection = new SqlConnection(SourceConnectionString);
        await using var targetConnection = new SqlConnection(TargetConnectionString);
        await sourceConnection.OpenAsync();
        await targetConnection.OpenAsync();

        var sourceSchema = await new SchemaReader().ReadSchemaAsync(sourceConnection);
        var targetSchema = await new SchemaReader().ReadSchemaAsync(targetConnection);
        var sourceTable = sourceSchema.Tables.Single(t => t.TableName == "Widgets");
        var targetTable = targetSchema.Tables.Single(t => t.TableName == "Widgets");

        var keyColumns = sourceTable.PrimaryKeyColumnsInOrder.Select(c => c.Name).ToList();
        var valueColumns = sourceTable.Columns.Select(c => c.Name).Except(keyColumns).ToList();

        var result = await new KeyedTableComparer().CompareAsync(
            sourceConnection, targetConnection, sourceTable, targetTable, keyColumns, valueColumns, maxExamplesPerCategory: 10);

        Assert.Equal(3, result.SourceRowCount);
        Assert.Equal(3, result.TargetRowCount);
        Assert.Equal(1, result.MatchedIdenticalCount); // Id=1
        Assert.Equal(1, result.ChangedRows.TotalCount); // Id=2
        Assert.Equal(1, result.RowsOnlyInSource.TotalCount); // Id=3
        Assert.Equal(1, result.RowsOnlyInTarget.TotalCount); // Id=4

        var changed = Assert.Single(result.ChangedRows.Examples);
        Assert.Equal(2, changed.KeyValues["Id"]);
        Assert.Contains("Amount", changed.ChangedColumnNames);
        Assert.DoesNotContain("Name", changed.ChangedColumnNames);

        var onlyInSource = Assert.Single(result.RowsOnlyInSource.Examples);
        Assert.Equal(3, onlyInSource.Values["Id"]);

        var onlyInTarget = Assert.Single(result.RowsOnlyInTarget.Examples);
        Assert.Equal(4, onlyInTarget.Values["Id"]);
    }

    [Fact]
    public async Task CompareAsync_IdenticalTables_ReportsIsIdentical()
    {
        await using var sourceConnection = new SqlConnection(SourceConnectionString);
        await sourceConnection.OpenAsync();

        var schema = await new SchemaReader().ReadSchemaAsync(sourceConnection);
        var table = schema.Tables.Single(t => t.TableName == "Widgets");
        var keyColumns = table.PrimaryKeyColumnsInOrder.Select(c => c.Name).ToList();
        var valueColumns = table.Columns.Select(c => c.Name).Except(keyColumns).ToList();

        // Compare the source table against itself — should be trivially identical.
        await using var secondSourceConnection = new SqlConnection(SourceConnectionString);
        await secondSourceConnection.OpenAsync();

        var result = await new KeyedTableComparer().CompareAsync(
            sourceConnection, secondSourceConnection, table, table, keyColumns, valueColumns, maxExamplesPerCategory: 10);

        Assert.True(result.IsIdentical);
        Assert.Equal(3, result.MatchedIdenticalCount);
    }

    [Fact]
    public async Task CompareAsync_VarbinaryMaxColumn_ComparesByHashAndDetectsChange()
    {
        await using var sourceConnection = new SqlConnection(SourceConnectionString);
        await using var targetConnection = new SqlConnection(TargetConnectionString);
        await sourceConnection.OpenAsync();
        await targetConnection.OpenAsync();

        var sourceSchema = await new SchemaReader().ReadSchemaAsync(sourceConnection);
        var targetSchema = await new SchemaReader().ReadSchemaAsync(targetConnection);
        var sourceTable = sourceSchema.Tables.Single(t => t.TableName == "Documents");
        var targetTable = targetSchema.Tables.Single(t => t.TableName == "Documents");

        var keyColumns = sourceTable.PrimaryKeyColumnsInOrder.Select(c => c.Name).ToList();
        var valueColumns = sourceTable.Columns.Select(c => c.Name).Except(keyColumns).ToList();

        var result = await new KeyedTableComparer().CompareAsync(
            sourceConnection, targetConnection, sourceTable, targetTable, keyColumns, valueColumns, maxExamplesPerCategory: 10);

        Assert.Equal(1, result.MatchedIdenticalCount); // Id=1, identical content
        Assert.Equal(1, result.ChangedRows.TotalCount); // Id=2, content differs

        var changed = Assert.Single(result.ChangedRows.Examples);
        Assert.Equal(2, changed.KeyValues["Id"]);
        Assert.Contains("Content", changed.ChangedColumnNames);

        // Only a 32-byte SHA2_256 hash ever crosses the wire for this column — never the real content.
        Assert.Equal(32, ((byte[])changed.SourceValues["Content"]!).Length);
        Assert.Equal(32, ((byte[])changed.TargetValues["Content"]!).Length);
    }

    [Fact]
    public async Task CompareAsync_XmlColumn_ComparesByHashAndDetectsChange()
    {
        await using var sourceConnection = new SqlConnection(SourceConnectionString);
        await using var targetConnection = new SqlConnection(TargetConnectionString);
        await sourceConnection.OpenAsync();
        await targetConnection.OpenAsync();

        var sourceSchema = await new SchemaReader().ReadSchemaAsync(sourceConnection);
        var targetSchema = await new SchemaReader().ReadSchemaAsync(targetConnection);
        var sourceTable = sourceSchema.Tables.Single(t => t.TableName == "XmlDocs");
        var targetTable = targetSchema.Tables.Single(t => t.TableName == "XmlDocs");

        var keyColumns = sourceTable.PrimaryKeyColumnsInOrder.Select(c => c.Name).ToList();
        var valueColumns = sourceTable.Columns.Select(c => c.Name).Except(keyColumns).ToList();

        var result = await new KeyedTableComparer().CompareAsync(
            sourceConnection, targetConnection, sourceTable, targetTable, keyColumns, valueColumns, maxExamplesPerCategory: 10);

        Assert.Equal(1, result.MatchedIdenticalCount); // Id=1, identical XML
        Assert.Equal(1, result.ChangedRows.TotalCount); // Id=2, XML content differs

        var changed = Assert.Single(result.ChangedRows.Examples);
        Assert.Equal(2, changed.KeyValues["Id"]);
        Assert.Contains("SourceXml", changed.ChangedColumnNames);

        // HASHBYTES can't take xml directly — confirms the CONVERT(nvarchar(max), ...) step actually
        // ran, since only a 32-byte SHA2_256 hash should ever cross the wire for this column.
        Assert.Equal(32, ((byte[])changed.SourceValues["SourceXml"]!).Length);
        Assert.Equal(32, ((byte[])changed.TargetValues["SourceXml"]!).Length);
    }

    private static async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"CREATE DATABASE [{databaseName}];", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string databaseName)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];", connection);
        await command.ExecuteNonQueryAsync();
    }
}
