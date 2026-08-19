namespace DataCompare.Engine.Models
{

    /// <summary>
    /// Non-secret connection details for one SQL Server endpoint (side A or side B of a comparison).
    /// The password itself is never stored here — it lives in the OS credential store, keyed by
    /// <see cref="CredentialTarget"/>.
    /// </summary>
    public sealed class ConnectionProfile
    {
        /// <summary>the display name of this connection profile.</summary>
        public required string Name { get; set; }

        /// <summary>the SQL Server instance name or address to connect to.</summary>
        public required string ServerName { get; set; }

        /// <summary>the initial catalog (database) to connect to, or null to connect without selecting a database.</summary>
        public string? DatabaseName { get; set; }

        /// <summary>the SQL Server Authentication user id to connect with.</summary>
        public required string UserId { get; set; }

        /// <summary>whether the connection should be encrypted. Defaults to true.</summary>
        public bool Encrypt { get; set; } = true;

        /// <summary>whether an untrusted server certificate should be accepted.</summary>
        public bool TrustServerCertificate { get; set; }

        /// <summary>whether the password for this connection should be remembered in the OS credential store.</summary>
        public bool RememberCredentials { get; set; }

        /// <summary>Key used to look up this connection's password in the OS credential store.</summary>
        public string CredentialTarget => $"DataCompare:{Name}:{UserId}";
    }
}
