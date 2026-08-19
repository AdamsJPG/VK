using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataCompare.Engine.Connections;
using DataCompare.Engine.Models;
using DataCompare.Engine.Security;

namespace DataCompare.App.ViewModels
{

    /// <summary>
    /// One side (Source or Target) of the connection setup screen: server/db/user fields, a password
    /// the user enters and optionally saves to the OS credential store, and a test-connection action.
    /// </summary>
    public partial class ConnectionSetupViewModel : ObservableObject
    {
        private readonly ICredentialStore _credentialStore;
        private readonly SqlConnectionFactory _connectionFactory;

        [ObservableProperty]
        private string _label = string.Empty;

        /// <summary>True for the Target (destination) side; false for Source. Fixed at construction.</summary>
        public bool IsTarget { get; init; }

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _serverName = string.Empty;

        [ObservableProperty]
        private string _databaseName = string.Empty;

        [ObservableProperty]
        private string _userId = string.Empty;

        [ObservableProperty]
        private bool _rememberCredentials;

        [ObservableProperty]
        private bool _encrypt = true;

        [ObservableProperty]
        private bool _trustServerCertificate;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _lastTestSucceeded;

        [ObservableProperty]
        private bool _isTesting;

        [ObservableProperty]
        private ObservableCollection<string> _availableDatabases = [];

        [ObservableProperty]
        private bool _isRefreshingDatabases;

        /// <summary>
        /// Constructs a connection setup ViewModel for one side (Source or Target).
        /// </summary>
        /// <param name="credentialStore">a DataCompare.Engine.Security.ICredentialStore implementation used to save and look up this side's remembered password</param>
        /// <param name="connectionFactory">a DataCompare.Engine.Connections.SqlConnectionFactory used to build connections and test/list databases for this side</param>
        public ConnectionSetupViewModel(ICredentialStore credentialStore, SqlConnectionFactory connectionFactory)
        {
            _credentialStore = credentialStore;
            _connectionFactory = connectionFactory;
        }

        /// <summary>Raised after a successful Test Connection — used to trigger auto-saving the last-used setup.</summary>
        public event EventHandler? ConnectionTestSucceeded;

        /// <summary>
        /// Builds a connection profile snapshot from this side's current field values.
        /// </summary>
        /// <returns>returns a DataCompare.Engine.Models.ConnectionProfile describing this side's current server/database/user/flags</returns>
        public ConnectionProfile ToProfile() => new()
        {
            Name = Name,
            ServerName = ServerName,
            DatabaseName = string.IsNullOrWhiteSpace(DatabaseName) ? null : DatabaseName,
            UserId = UserId,
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate,
            RememberCredentials = RememberCredentials,
        };

        /// <summary>
        /// Populates this side's fields from a saved connection profile. Does not touch the password —
        /// that's looked up separately via <see cref="TryGetRememberedPassword"/>.
        /// </summary>
        /// <param name="profile">a DataCompare.Engine.Models.ConnectionProfile holding the server/database/user/flags to load</param>
        /// <returns>returns nothing; this is a System.Void method</returns>
        public void LoadFrom(ConnectionProfile profile)
        {
            ServerName = profile.ServerName;
            DatabaseName = profile.DatabaseName ?? string.Empty;
            UserId = profile.UserId;
            Encrypt = profile.Encrypt;
            TrustServerCertificate = profile.TrustServerCertificate;
            RememberCredentials = profile.RememberCredentials;
        }

        /// <summary>Looks up this connection's remembered password from the OS credential store — only
        /// returns a value when "Remember credentials" is checked, matching <see cref="SaveCredentialIfRemembered"/>.</summary>
        /// <returns>returns a nullable System.String holding the remembered password, or null when "Remember credentials" is unchecked or no password is stored</returns>
        public string? TryGetRememberedPassword() =>
            RememberCredentials ? _credentialStore.TryGetPassword(ToProfile().CredentialTarget) : null;

        /// <summary>Copies the other side's field values (server, database, user, flags) — used by the
        /// copy-across-arrow actions. Passwords are never copied — they live only in each side's own
        /// PasswordBox / credential store entry.</summary>
        /// <param name="other">a DataCompare.App.ViewModels.ConnectionSetupViewModel whose field values are copied into this instance</param>
        /// <returns>returns nothing; this is a System.Void method</returns>
        public void CopyFieldsFrom(ConnectionSetupViewModel other)
        {
            ServerName = other.ServerName;
            DatabaseName = other.DatabaseName;
            UserId = other.UserId;
            Encrypt = other.Encrypt;
            TrustServerCertificate = other.TrustServerCertificate;
            RememberCredentials = other.RememberCredentials;
        }

        /// <summary>Persists the given password to the OS credential store, but only when the user has
        /// checked "Remember credentials" — otherwise the password is used for this session only.</summary>
        /// <param name="password">a System.String holding the plaintext password to save, when remembering is enabled</param>
        /// <returns>returns nothing; this is a System.Void method</returns>
        public void SaveCredentialIfRemembered(string password)
        {
            if (RememberCredentials && !string.IsNullOrEmpty(password))
            {
                var profile = ToProfile();
                _credentialStore.SavePassword(profile.CredentialTarget, profile.UserId, password);
            }
        }

        // Database is deliberately not required — a blank InitialCatalog is a legitimate connection
        // (uses the login's default database), and the picker is just a convenience, not a gate.
        /// <summary>
        /// Determines whether this side has the minimum fields filled in to attempt a connection.
        /// </summary>
        /// <returns>returns a System.Boolean that is true when both Server and User name are non-blank</returns>
        public bool HasRequiredFieldsFilled() =>
            !string.IsNullOrWhiteSpace(ServerName) && !string.IsNullOrWhiteSpace(UserId);

        /// <summary>
        /// Resets this side's fields and status back to their defaults.
        /// </summary>
        /// <returns>returns nothing; this is a System.Void method</returns>
        public void ClearFields()
        {
            ServerName = string.Empty;
            DatabaseName = string.Empty;
            UserId = string.Empty;
            RememberCredentials = false;
            Encrypt = true;
            TrustServerCertificate = false;
            StatusMessage = string.Empty;
            LastTestSucceeded = false;
        }

        /// <summary>
        /// Connects with the current field values and password, then lists the databases available on
        /// the server, populating <see cref="AvailableDatabases"/>.
        /// </summary>
        /// <param name="password">a System.String holding the password to connect with</param>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous refresh operation</returns>
        [RelayCommand]
        private async Task RefreshDatabasesAsync(string password)
        {
            IsRefreshingDatabases = true;
            StatusMessage = "Loading databases...";
            try
            {
                var profile = ToProfile();
                SaveCredentialIfRemembered(password);

                await using var connection = _connectionFactory.CreateConnection(profile, password);
                await connection.OpenAsync();
                var databases = await DatabaseLister.ListDatabasesAsync(connection);

                AvailableDatabases = new ObservableCollection<string>(databases);
                StatusMessage = $"Found {databases.Count} database(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to list databases: {ex.Message}";
            }
            finally
            {
                IsRefreshingDatabases = false;
            }
        }

        /// <summary>
        /// Tests the connection with the current field values and password, updating <see
        /// cref="LastTestSucceeded"/> and <see cref="StatusMessage"/>, and raising <see
        /// cref="ConnectionTestSucceeded"/> on success.
        /// </summary>
        /// <param name="password">a System.String holding the password to connect with</param>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous test operation</returns>
        [RelayCommand]
        private async Task TestConnectionAsync(string password)
        {
            IsTesting = true;
            StatusMessage = "Testing...";
            try
            {
                var profile = ToProfile();
                SaveCredentialIfRemembered(password);

                var result = await _connectionFactory.TestConnectionAsync(profile, password);
                LastTestSucceeded = result.Success;
                StatusMessage = result.Success
                    ? $"Connected. Server version {result.ServerVersion}."
                    : $"Failed: {result.Message}";

                if (result.Success)
                {
                    ConnectionTestSucceeded?.Invoke(this, EventArgs.Empty);
                }
            }
            finally
            {
                IsTesting = false;
            }
        }
    }
}
