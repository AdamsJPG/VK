namespace DataCompare.Engine.Models;

/// <summary>
/// Non-secret connection details for one SQL Server endpoint (side A or side B of a comparison).
/// The password itself is never stored here — it lives in the OS credential store, keyed by
/// <see cref="CredentialTarget"/>.
/// </summary>
public sealed class ConnectionProfile
{
    public required string Name { get; set; }
    public required string ServerName { get; set; }
    public string? DatabaseName { get; set; }
    public required string UserId { get; set; }
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; }
    public bool RememberCredentials { get; set; }

    /// <summary>Key used to look up this connection's password in the OS credential store.</summary>
    public string CredentialTarget => $"DataCompare:{Name}:{UserId}";
}
