using System.Text.Json;
using DataCompare.Engine.Models;

namespace DataCompare.Engine.Profiles;

/// <summary>
/// Loads/saves <see cref="ComparisonProfile"/> JSON files under %AppData%\DataCompare\profiles.
/// Profiles never contain passwords — those live in <see cref="Security.ICredentialStore"/>.
/// </summary>
public sealed class ComparisonProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _profilesDirectory;

    public ComparisonProfileStore(string? profilesDirectory = null)
    {
        _profilesDirectory = profilesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DataCompare", "profiles");
        Directory.CreateDirectory(_profilesDirectory);
    }

    public async Task SaveAsync(ComparisonProfile profile, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(GetPath(profile.Name));
        await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
    }

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

    public IReadOnlyList<string> ListProfileNames() =>
        Directory.EnumerateFiles(_profilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private string GetPath(string name) => Path.Combine(_profilesDirectory, $"{SanitizeFileName(name)}.json");

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
    }
}
