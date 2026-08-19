using DataCompare.Engine.DataComparison;
using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Tests.DataComparison
{

    /// <summary>
    /// Runs the on-demand large-content drill-down fetch against real SQL Server (LocalDB). Requires
    /// SQL Server LocalDB.
    /// </summary>
    public sealed class LargeContentValueFetcherIntegrationTests : IAsyncLifetime
    {
        private const string MasterConnectionString = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;";
        private readonly string _databaseName = $"DataCompareTests_LC_{Guid.NewGuid():N}";

        private string ConnectionString => $@"Server=(localdb)\MSSQLLocalDB;Database={_databaseName};Integrated Security=true;";

        /// <summary>
        /// creates the LocalDB test database and seeds the Documents table used by the fetch tests.
        /// </summary>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous initialization operation</returns>
        public async Task InitializeAsync()
        {
            await using var masterConnection = new SqlConnection(MasterConnectionString);
            await masterConnection.OpenAsync();
            await using var createDatabase = new SqlCommand($"CREATE DATABASE [{_databaseName}];", masterConnection);
            await createDatabase.ExecuteNonQueryAsync();

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var createTable = new SqlCommand("""
                CREATE TABLE dbo.Documents (Id INT PRIMARY KEY, Content VARBINARY(MAX) NULL);
                INSERT INTO dbo.Documents (Id, Content) VALUES (1, 0x25504446313233), (2, NULL);
                """, connection);
            await createTable.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// drops the LocalDB test database created for this test class.
        /// </summary>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous cleanup operation</returns>
        public async Task DisposeAsync()
        {
            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];", connection);
            await command.ExecuteNonQueryAsync();
        }

        [Fact]
        public async Task FetchValueAsync_ExistingRow_ReturnsRawBytes()
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            var table = (await new SchemaReader().ReadSchemaAsync(connection)).Tables.Single(t => t.TableName == "Documents");

            var bytes = await new LargeContentValueFetcher().FetchValueAsync(
                connection, table, "Content", new Dictionary<string, object?> { ["Id"] = 1 });

            Assert.Equal([0x25, 0x50, 0x44, 0x46, 0x31, 0x32, 0x33], bytes);
        }

        [Fact]
        public async Task FetchValueAsync_NullValue_ReturnsNull()
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            var table = (await new SchemaReader().ReadSchemaAsync(connection)).Tables.Single(t => t.TableName == "Documents");

            var bytes = await new LargeContentValueFetcher().FetchValueAsync(
                connection, table, "Content", new Dictionary<string, object?> { ["Id"] = 2 });

            Assert.Null(bytes);
        }

        [Fact]
        public async Task FetchValueAsync_RowNoLongerExists_ReturnsNull()
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            var table = (await new SchemaReader().ReadSchemaAsync(connection)).Tables.Single(t => t.TableName == "Documents");

            var bytes = await new LargeContentValueFetcher().FetchValueAsync(
                connection, table, "Content", new Dictionary<string, object?> { ["Id"] = 999 });

            Assert.Null(bytes);
        }
    }
}
