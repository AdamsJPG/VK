using System.Text.Json;
using DataCompare.Engine.Models;

namespace DataCompare.Engine.Profiles
{

    /// <summary>
    /// Loads/saves <see cref="ComparisonProfile"/> JSON files under %AppData%\DataCompare\profiles.
    /// Profiles never contain passwords — those live in <see cref="Security.ICredentialStore"/>.
    /// </summary>
    public sealed class ComparisonProfileStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly string _profilesDirectory;

        /// <summary>
        /// the ingredients for my class are as follows...
        /// </summary>
        /// <param name="profilesDirectory">a System.String containing the directory in which profile JSON files are stored, or null to use the default %AppData%\DataCompare\profiles directory.</param>
        public ComparisonProfileStore(string? profilesDirectory = null)
        {
            _profilesDirectory = profilesDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DataCompare", "profiles");
            Directory.CreateDirectory(_profilesDirectory);
        }

        /// <summary>
        /// saves the given profile to a JSON file named after the profile.
        /// </summary>
        /// <param name="profile">a DataCompare.Engine.Models.ComparisonProfile to persist.</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the save operation.</param>
        /// <returns>returns a System.Threading.Tasks.Task representing the asynchronous save operation.</returns>
        public async Task SaveAsync(ComparisonProfile profile, CancellationToken cancellationToken = default)
        {
            await using var stream = File.Create(GetPath(profile.Name));
            await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
        }

        /// <summary>
        /// loads the profile with the given name, if it exists.
        /// </summary>
        /// <param name="name">a System.String containing the name of the profile to load.</param>
        /// <param name="cancellationToken">a System.Threading.CancellationToken used to cancel the load operation.</param>
        /// <returns>returns a System.Threading.Tasks.Task of DataCompare.Engine.Models.ComparisonProfile containing the loaded profile, or null if no profile with that name exists.</returns>
        public async Task<ComparisonProfile?> LoadAsync(string name, CancellationToken cancellationToken = default)
        {
            var path = GetPath(name);
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ComparisonProfile>(stream, JsonOptions, cancellationToken);
        }

        /// <summary>
        /// lists the names of all saved comparison profiles.
        /// </summary>
        /// <returns>returns a System.Collections.Generic.IReadOnlyList of System.String containing the profile names, ordered alphabetically.</returns>
        public IReadOnlyList<string> ListProfileNames() =>
            Directory.EnumerateFiles(_profilesDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// builds the full file path for the profile with the given name.
        /// </summary>
        /// <param name="name">a System.String containing the name of the profile.</param>
        /// <returns>returns a System.String containing the full path to the profile's JSON file.</returns>
        private string GetPath(string name) => Path.Combine(_profilesDirectory, $"{SanitizeFileName(name)}.json");

        /// <summary>
        /// replaces any characters that are invalid in a file name with an underscore.
        /// </summary>
        /// <param name="name">a System.String containing the name to sanitize.</param>
        /// <returns>returns a System.String containing the sanitized file name.</returns>
        private static string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        }
    }
}
