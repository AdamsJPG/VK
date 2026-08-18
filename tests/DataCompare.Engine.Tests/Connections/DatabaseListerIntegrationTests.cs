using DataCompare.Engine.Connections;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Tests.Connections;

/// <summary>Runs against real SQL Server (LocalDB) — confirms the generated query is valid T-SQL
/// and actually excludes system databases. Requires SQL Server LocalDB.</summary>
public sealed class DatabaseListerIntegrationTests : IAsyncLifetime
{
    private const string MasterConnectionString = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;";
    private readonly string _databaseName = $"DataCompareTests_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"CREATE DATABASE [{_databaseName}];", connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ListDatabasesAsync_AgainstRealSqlServer_IncludesCreatedDatabaseAndExcludesSystemDatabases()
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();

        var databases = await DatabaseLister.ListDatabasesAsync(connection);

        Assert.Contains(_databaseName, databases);
        Assert.DoesNotContain("master", databases);
        Assert.DoesNotContain("tempdb", databases);
        Assert.DoesNotContain("model", databases);
        Assert.DoesNotContain("msdb", databases);
    }
}
