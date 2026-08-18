using DataCompare.Engine.Models;
using DataCompare.Engine.Profiles;

namespace DataCompare.Engine.Tests.Profiles;

public sealed class ComparisonProfileStoreTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"DataCompareTests_{Guid.NewGuid()}");

    private ComparisonProfileStore CreateStore() => new(_tempDirectory);

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsProfile()
    {
        var store = CreateStore();
        var profile = new ComparisonProfile
        {
            Name = "Nightly Regression",
            ConnectionA = new ConnectionProfile { Name = "A", ServerName = "srv-a", UserId = "user_a" },
            ConnectionB = new ConnectionProfile { Name = "B", ServerName = "srv-b", UserId = "user_b" },
        };

        await store.SaveAsync(profile);
        var loaded = await store.LoadAsync(profile.Name);

        Assert.NotNull(loaded);
        Assert.Equal(profile.Name, loaded!.Name);
        Assert.Equal(profile.ConnectionA.ServerName, loaded.ConnectionA.ServerName);
        Assert.Equal(profile.ConnectionB.UserId, loaded.ConnectionB.UserId);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenProfileDoesNotExist()
    {
        var store = CreateStore();

        var loaded = await store.LoadAsync("does-not-exist");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task ListProfileNames_ReturnsSavedProfiles()
    {
        var store = CreateStore();
        var profile = new ComparisonProfile
        {
            Name = "Profile One",
            ConnectionA = new ConnectionProfile { Name = "A", ServerName = "srv-a", UserId = "user_a" },
            ConnectionB = new ConnectionProfile { Name = "B", ServerName = "srv-b", UserId = "user_b" },
        };
        await store.SaveAsync(profile);

        var names = store.ListProfileNames();

        Assert.Contains("Profile One", names);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
