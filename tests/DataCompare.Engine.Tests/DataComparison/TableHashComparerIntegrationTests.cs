using DataCompare.Engine.DataComparison;
using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Tests.DataComparison;

/// <summary>
/// Runs the generated SQL against a real SQL Server (LocalDB) rather than just asserting on the
/// SQL text — string-shape assertions can't catch things like a column alias that happens to be a
/// reserved word on a given server version. Requires SQL Server LocalDB (ships with SSMS/VS/SQL
/// Server Express tools, so present on any dev machine set up for this project).
/// </summary>
public sealed class TableHashComparerIntegrationTests : IAsyncLifetime
{
    private const string MasterConnectionString = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;";
    private readonly string _databaseName = $"DataCompareTests_{Guid.NewGuid():N}";

    private string DatabaseConnectionString =>
        $@"Server=(localdb)\MSSQLLocalDB;Database={_databaseName};Integrated Security=true;";

    public async Task InitializeAsync()
    {
        await using (var connection = new SqlConnection(MasterConnectionString))
        {
            await connection.OpenAsync();
            await using var createDatabase = new SqlCommand($"CREATE DATABASE [{_databaseName}];", connection);
            await createDatabase.ExecuteNonQueryAsync();
        }

        await using var dbConnection = new SqlConnection(DatabaseConnectionString);
        await dbConnection.OpenAsync();
        await using var createTable = new SqlCommand("""
            CREATE TABLE dbo.Widgets (
                Id INT IDENTITY PRIMARY KEY,
                Name NVARCHAR(50) NOT NULL,
                Created DATETIME2 NULL,
                Amount DECIMAL(10,2) NULL
            );
            INSERT INTO dbo.Widgets (Name, Created, Amount) VALUES
                ('Alice', '2024-01-01T10:00:00', 10.50),
                ('Alice', '2024-01-01T10:00:00', 10.50),
                ('Bob', NULL, NULL);
            """, dbConnection);
        await createTable.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var dropDatabase = new SqlCommand(
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];",
            connection);
        await dropDatabase.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ReadHashCountsAsync_AgainstRealSqlServer_GroupsDuplicateRowsCorrectly()
    {
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();

        var schema = await new SchemaReader().ReadSchemaAsync(connection);
        var table = schema.Tables.Single(t => t.TableName == "Widgets");
        var columnNames = NonIdentityColumnNames(table);

        var counts = await new TableHashComparer().ReadHashCountsAsync(connection, table, columnNames);

        // Id is an identity column, so it's excluded here — otherwise the two "identical" Alice
        // rows would never collide, since each row's auto-generated Id is unique by definition.
        Assert.Equal(2, counts.Count); // one hash for the 2 identical Alice rows, one for Bob
        Assert.Contains(counts.Values, c => c == 2);
        Assert.Contains(counts.Values, c => c == 1);
    }

    [Fact]
    public async Task FetchSampleRowsAsync_AgainstRealSqlServer_ReturnsRowsMatchingTheHash()
    {
        await using var connection = new SqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();

        var schema = await new SchemaReader().ReadSchemaAsync(connection);
        var table = schema.Tables.Single(t => t.TableName == "Widgets");
        var columnNames = NonIdentityColumnNames(table);

        var counts = await new TableHashComparer().ReadHashCountsAsync(connection, table, columnNames);
        var aliceHash = counts.Single(kv => kv.Value == 2).Key;

        var samples = await new RowDrillDownFetcher().FetchSampleRowsAsync(
            connection, table, columnNames, aliceHash, sampleSize: 10);

        Assert.Equal(2, samples.Count);
        Assert.All(samples, row => Assert.Equal("Alice", row["Name"]));
    }

    private static List<string> NonIdentityColumnNames(TableSchema table) =>
        table.Columns.Where(c => !c.IsIdentity).Select(c => c.Name).ToList();
}
