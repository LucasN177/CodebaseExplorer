using CodebaseExplorer.Storage.Persistence.Entities;
using CodebaseExplorer.Storage.Stores;

namespace CodebaseExplorer.Storage.Tests.Stores;

/// <summary>STORE-005: user settings are stored as JSON strings and survive a new store instance.</summary>
public sealed class UserSettingsStoreTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly SettableTimeProvider _time = new(new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero));
    private readonly UserSettingsStore _store;

    public UserSettingsStoreTests() => _store = new UserSettingsStore(new TestContextFactory(_database), _time);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task MissingKey_ReadsAsNull()
    {
        Assert.Null(await _store.GetAsync("appearance.theme", Token));
    }

    [Fact]
    public async Task StoredValue_IsReadByANewStoreInstance()
    {
        await _store.SetAsync("appearance.theme", "dark", Token);

        var restarted = new UserSettingsStore(new TestContextFactory(_database), _time);

        Assert.Equal("dark", await restarted.GetAsync("appearance.theme", Token));
    }

    [Fact]
    public async Task SettingAgain_ReplacesValueAndTimestamp()
    {
        await _store.SetAsync("appearance.theme", "dark", Token);
        _time.Now += TimeSpan.FromMinutes(5);
        await _store.SetAsync("appearance.theme", "light", Token);

        await using var context = _database.CreateContext();
        var record = Assert.Single(context.UserSettings);
        Assert.Equal("\"light\"", record.ValueJson);
        Assert.Equal(_time.GetUtcNow(), record.UpdatedAt);
        Assert.Equal("light", await _store.GetAsync("appearance.theme", Token));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("{\"a\":1}")]
    [InlineData("not json")]
    public async Task ValueThatIsNotAJsonString_ReadsAsNull(string valueJson)
    {
        await using (var context = _database.CreateContext())
        {
            context.UserSettings.Add(new UserSettingRecord { Key = "appearance.theme", ValueJson = valueJson, UpdatedAt = _time.GetUtcNow() });
            await context.SaveChangesAsync(Token);
        }

        Assert.Null(await _store.GetAsync("appearance.theme", Token));
    }
}
