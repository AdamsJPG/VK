using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Connections
{

    /// <summary>Lists user databases on a connected SQL Server instance, for populating a database picker.</summary>
    public static class DatabaseLister
    {
        private const string Query = """
            SELECT name
            FROM sys.databases
            WHERE database_id > 4 AND state = 0
            ORDER BY name;
            """;

        /// <summary>
        /// lists the user databases visible on the given connection.
        /// </summary>
        /// <param name="connection">a Microsoft.Data.SqlClient.SqlConnection that is already open against the target SQL Server instance.</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the query.</param>
        /// <returns>returns a System.Threading.Tasks.Task of System.Collections.Generic.List of System.String containing the user database names, ordered by name.</returns>
        public static async Task<List<string>> ListDatabasesAsync(SqlConnection connection, CancellationToken cancellationToken = default)
        {
            var databases = new List<string>();
            await using var command = new SqlCommand(Query, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                databases.Add(reader.GetString(0));
            }

            return databases;
        }
    }
}
