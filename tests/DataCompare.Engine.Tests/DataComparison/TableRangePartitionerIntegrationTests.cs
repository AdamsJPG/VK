using DataCompare.Engine.DataComparison;
using DataCompare.Engine.Schema;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Tests.DataComparison
{

    /// <summary>
    /// Verifies that comparing a table in range-partitioned chunks and combining the results (planning.md
    /// §19) finds exactly the same differences as comparing the whole table in one pass. Requires SQL
    /// Server LocalDB.
    /// </summary>
    public sealed class TableRangePartitionerIntegrationTests : IAsyncLifetime
    {
        private const string MasterConnectionString = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;";
        private readonly string _sourceDatabaseName = $"DataCompareTests_PSrc_{Guid.NewGuid():N}";
        private readonly string _targetDatabaseName = $"DataCompareTests_PTgt_{Guid.NewGuid():N}";

        private string SourceConnectionString => $@"Server=(localdb)\MSSQLLocalDB;Database={_sourceDatabaseName};Integrated Security=true;";
        private string TargetConnectionString => $@"Server=(localdb)\MSSQLLocalDB;Database={_targetDatabaseName};Integrated Security=true;";

        /// <summary>
        /// creates the source and target LocalDB test databases and seeds each with a Widgets table
        /// whose rows deliberately diverge in a way that spans multiple partition ranges.
        /// </summary>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous initialization operation</returns>
        public async Task InitializeAsync()
        {
            await CreateDatabaseAsync(_sourceDatabaseName);
            await CreateDatabaseAsync(_targetDatabaseName);

            await using var sourceConnection = new SqlConnection(SourceConnectionString);
            await sourceConnection.OpenAsync();
            await using var createSourceTable = new SqlCommand("""
                CREATE TABLE dbo.Widgets (Id INT PRIMARY KEY, Amount DECIMAL(10,2) NOT NULL);
                DECLARE @i INT = 1;
                WHILE @i <= 100
                BEGIN
                    INSERT INTO dbo.Widgets (Id, Amount) VALUES (@i, @i * 1.0);
                    SET @i += 1;
                END
                """, sourceConnection);
            await createSourceTable.ExecuteNonQueryAsync();

            await using var targetConnection = new SqlConnection(TargetConnectionString);
            await targetConnection.OpenAsync();
            await using var createTargetTable = new SqlCommand("""
                CREATE TABLE dbo.Widgets (Id INT PRIMARY KEY, Amount DECIMAL(10,2) NOT NULL);
                DECLARE @i INT = 1;
                WHILE @i <= 100
                BEGIN
                    -- Every 10th row's amount diverges from source; rows 96-100 don't exist on target;
                    -- these deliberately land in different chunks once partitioned.
                    IF @i % 10 = 0
                        INSERT INTO dbo.Widgets (Id, Amount) VALUES (@i, @i * 2.0);
                    ELSE IF @i <= 95
                        INSERT INTO dbo.Widgets (Id, Amount) VALUES (@i, @i * 1.0);
                    SET @i += 1;
                END
                """, targetConnection);
            await createTargetTable.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// drops the source and target LocalDB test databases created for this test class.
        /// </summary>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous cleanup operation</returns>
        public async Task DisposeAsync()
        {
            await DropDatabaseAsync(_sourceDatabaseName);
            await DropDatabaseAsync(_targetDatabaseName);
        }

        [Fact]
        public async Task PartitionedComparison_CombinedResult_MatchesWholeTableComparison()
        {
            await using var sourceConnection = new SqlConnection(SourceConnectionString);
            await using var targetConnection = new SqlConnection(TargetConnectionString);
            await sourceConnection.OpenAsync();
            await targetConnection.OpenAsync();

            var sourceTable = (await new SchemaReader().ReadSchemaAsync(sourceConnection)).Tables.Single(t => t.TableName == "Widgets");
            var targetTable = (await new SchemaReader().ReadSchemaAsync(targetConnection)).Tables.Single(t => t.TableName == "Widgets");
            var keyColumns = sourceTable.PrimaryKeyColumnsInOrder.Select(c => c.Name).ToList();
            var valueColumns = sourceTable.Columns.Select(c => c.Name).Except(keyColumns).ToList();
            var comparer = new KeyedTableComparer();

            var wholeTableResult = await comparer.CompareAsync(
                sourceConnection, targetConnection, sourceTable, targetTable, keyColumns, valueColumns, maxExamplesPerCategory: 100);

            var boundaries = await new TableRangePartitioner().ComputeBoundariesAsync(
                sourceConnection, sourceTable, keyColumns[0], chunkCount: 4, approximateRowCount: 100);
            var ranges = TableRangePartitioner.BuildRanges(keyColumns[0], boundaries);
            Assert.True(ranges.Count > 1, "Expected the 100-row table to actually split into multiple ranges.");

            var partialResults = new List<KeyedTableDiffResult>();
            foreach (var range in ranges)
            {
                await using var chunkSourceConnection = new SqlConnection(SourceConnectionString);
                await using var chunkTargetConnection = new SqlConnection(TargetConnectionString);
                await chunkSourceConnection.OpenAsync();
                await chunkTargetConnection.OpenAsync();

                partialResults.Add(await comparer.CompareAsync(
                    chunkSourceConnection, chunkTargetConnection, sourceTable, targetTable, keyColumns, valueColumns,
                    100, range));
            }

            var combined = KeyedTableDiffResult.Combine(sourceTable.FullName, partialResults, maxExamplesPerCategory: 100);

            Assert.Equal(wholeTableResult.SourceRowCount, combined.SourceRowCount);
            Assert.Equal(wholeTableResult.TargetRowCount, combined.TargetRowCount);
            Assert.Equal(wholeTableResult.MatchedIdenticalCount, combined.MatchedIdenticalCount);
            Assert.Equal(wholeTableResult.RowsOnlyInSource.TotalCount, combined.RowsOnlyInSource.TotalCount);
            Assert.Equal(wholeTableResult.ChangedRows.TotalCount, combined.ChangedRows.TotalCount);

            // Sanity-check the numbers are what the seed data actually implies, not just self-consistent.
            Assert.Equal(4, combined.RowsOnlyInSource.TotalCount); // Ids 96-99 missing from target
            Assert.Equal(10, combined.ChangedRows.TotalCount); // every 10th id (10, 20, ..., 100) has a different Amount
            Assert.Equal(86, combined.MatchedIdenticalCount);
        }

        /// <summary>
        /// creates a LocalDB database with the given name.
        /// </summary>
        /// <param name="databaseName">a System.String object giving the name of the database to create</param>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous create operation</returns>
        private static async Task CreateDatabaseAsync(string databaseName)
        {
            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{databaseName}];", connection);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// drops the LocalDB database with the given name.
        /// </summary>
        /// <param name="databaseName">a System.String object giving the name of the database to drop</param>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous drop operation</returns>
        private static async Task DropDatabaseAsync(string databaseName)
        {
            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
