namespace DataCompare.Engine.Security
{

    /// <summary>
    /// Stores/retrieves SQL Server Authentication passwords outside of profile JSON files.
    /// </summary>
    public interface ICredentialStore
    {
        /// <summary>
        /// saves the given password under the given target, associated with the given user id.
        /// </summary>
        /// <param name="target">a System.String identifying the credential to save, unique to a connection profile.</param>
        /// <param name="userId">a System.String containing the user id the password belongs to.</param>
        /// <param name="password">a System.String containing the password to save.</param>
        void SavePassword(string target, string userId, string password);

        /// <summary>
        /// attempts to retrieve the password saved under the given target.
        /// </summary>
        /// <param name="target">a System.String identifying the credential to retrieve.</param>
        /// <returns>returns a System.String containing the stored password, or null if no credential exists for <paramref name="target"/>.</returns>
        string? TryGetPassword(string target);

        /// <summary>
        /// deletes the password saved under the given target, if one exists.
        /// </summary>
        /// <param name="target">a System.String identifying the credential to delete.</param>
        void DeletePassword(string target);
    }
}
