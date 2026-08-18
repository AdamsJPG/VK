namespace DataCompare.Engine.Security;

/// <summary>
/// Stores/retrieves SQL Server Authentication passwords outside of profile JSON files.
/// </summary>
public interface ICredentialStore
{
    void SavePassword(string target, string userId, string password);

    /// <returns>The stored password, or null if no credential exists for <paramref name="target"/>.</returns>
    string? TryGetPassword(string target);

    void DeletePassword(string target);
}
