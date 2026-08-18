using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataCompare.Engine.Connections;
using DataCompare.Engine.Models;
using DataCompare.Engine.Security;

namespace DataCompare.App.ViewModels;

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

    public ConnectionSetupViewModel(ICredentialStore credentialStore, SqlConnectionFactory connectionFactory)
    {
        _credentialStore = credentialStore;
        _connectionFactory = connectionFactory;
    }

    /// <summary>Raised after a successful Test Connection — used to trigger auto-saving the last-used setup.</summary>
    public event EventHandler? ConnectionTestSucceeded;

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
    public string? TryGetRememberedPassword() =>
        RememberCredentials ? _credentialStore.TryGetPassword(ToProfile().CredentialTarget) : null;

    /// <summary>Copies the other side's field values (server, database, user, flags) — used by the
    /// copy-across-arrow actions. Passwords are never copied — they live only in each side's own
    /// PasswordBox / credential store entry.</summary>
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
    public bool HasRequiredFieldsFilled() =>
        !string.IsNullOrWhiteSpace(ServerName) && !string.IsNullOrWhiteSpace(UserId);

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
