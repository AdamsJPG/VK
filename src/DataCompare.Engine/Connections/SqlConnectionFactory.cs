using DataCompare.Engine.Models;
using Microsoft.Data.SqlClient;

namespace DataCompare.Engine.Connections;

/// <summary>
/// Builds <see cref="SqlConnection"/> instances for a <see cref="ConnectionProfile"/>. The password
/// is always supplied explicitly by the caller (never read implicitly from the OS credential
/// store) — whether it's also persisted to <see cref="Security.ICredentialStore"/> for next time is
/// a separate decision the caller makes (e.g. a "remember credentials" checkbox).
/// </summary>
public sealed class SqlConnectionFactory
{
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
        };

        return builder.ConnectionString;
    }

    public SqlConnection CreateConnection(ConnectionProfile profile, string password) =>
        new(BuildConnectionString(profile, password));

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
