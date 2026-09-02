using DataCompare.Engine.Models;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Connections
{

    /// <summary>
    /// Builds <see cref="SqlConnection"/> instances for a <see cref="ConnectionProfile"/>. The password
    /// is always supplied explicitly by the caller (never read implicitly from the OS credential
    /// store) — whether it's also persisted to <see cref="Security.ICredentialStore"/> for next time is
    /// a separate decision the caller makes (e.g. a "remember credentials" checkbox).
    /// </summary>
    public sealed class SqlConnectionFactory
    {
        /// <summary>
        /// builds a SQL Server connection string for the given profile and password.
        /// </summary>
        /// <param name="profile">a DataCompare.Engine.Models.ConnectionProfile describing the target server and options.</param>
        /// <param name="password">a System.String containing the SQL Server Authentication password to use.</param>
        /// <returns>returns a System.String containing the fully built connection string.</returns>
        public string BuildConnectionString(ConnectionProfile profile, string password)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = profile.ServerName,
                InitialCatalog = profile.DatabaseName ?? string.Empty,
                UserID = profile.UserId,
                Password = password,
                Encrypt = profile.Encrypt,
                TrustServerCertificate = profile.TrustServerCertificate,
                ConnectTimeout = 15,
                // Every SqlCommand created from a connection opened with this string inherits this as
                // its default CommandTimeout. ADO.NET's default of 30 seconds is tuned for OLTP queries,
                // not this tool's actual workload (full-table/view scans, hash-multiset diffs on
                // keyless objects — see TableHashComparer — and large-table range partitioning), so real
                // runs were hitting "Execution Timeout Expired" on perfectly healthy queries that just
                // take longer than 30s. 0 disables ADO.NET's own timeout entirely; the app already has
                // its own cooperative cancellation (the Cancel button), which is the actual mechanism
                // for "this is taking too long" here, not an arbitrary server-side clock.
                CommandTimeout = 0,
            };

            return builder.ConnectionString;
        }

        /// <summary>
        /// creates a new, unopened connection for the given profile and password.
        /// </summary>
        /// <param name="profile">a DataCompare.Engine.Models.ConnectionProfile describing the target server and options.</param>
        /// <param name="password">a System.String containing the SQL Server Authentication password to use.</param>
        /// <returns>returns a Microsoft.Data.SqlClient.SqlConnection built from the profile and password.</returns>
        public SqlConnection CreateConnection(ConnectionProfile profile, string password) =>
            new(BuildConnectionString(profile, password));

        /// <summary>
        /// attempts to open a connection for the given profile and password, to verify the connection details are correct.
        /// </summary>
        /// <param name="profile">a DataCompare.Engine.Models.ConnectionProfile describing the target server and options.</param>
        /// <param name="password">a System.String containing the SQL Server Authentication password to use.</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the connection attempt.</param>
        /// <returns>returns a System.Threading.Tasks.Task of DataCompare.Engine.Connections.ConnectionTestResult describing whether the connection succeeded.</returns>
        public async Task<ConnectionTestResult> TestConnectionAsync(
            ConnectionProfile profile, string password, CancellationToken cancellationToken = default)
        {
            try
            {
                await using var connection = CreateConnection(profile, password);
                await connection.OpenAsync(cancellationToken);
                return ConnectionTestResult.Ok(connection.ServerVersion);
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException)
            {
                return ConnectionTestResult.Failed(ex.Message);
            }
        }
    }
}
