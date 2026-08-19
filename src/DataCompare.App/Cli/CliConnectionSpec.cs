namespace DataCompare.App.Cli
{

    /// <summary>
    /// One side (source or target) of a CLI request JSON's connection details. The password is a
    /// plaintext field in this file by explicit design choice for CLI mode — unlike the GUI, which
    /// keeps passwords out of any saved file via the OS credential store — since a CLI invocation
    /// needs a fully non-interactive, scriptable input with no prior "remember credentials" step.
    /// </summary>
    public sealed class CliConnectionSpec
    {
        /// <summary>the SQL Server instance name or address to connect to.</summary>
        public required string Server { get; set; }

        /// <summary>the initial catalog (database) to connect to, or null to connect without selecting a database.</summary>
        public string? Database { get; set; }

        /// <summary>the SQL Server Authentication user id to connect with.</summary>
        public required string Username { get; set; }

        /// <summary>the SQL Server Authentication password to connect with, stored in plaintext in this file.</summary>
        public required string Password { get; set; }

        /// <summary>whether the connection should be encrypted. Defaults to true.</summary>
        public bool Encrypt { get; set; } = true;

        /// <summary>whether an untrusted server certificate should be accepted.</summary>
        public bool TrustServerCertificate { get; set; }
    }
}
