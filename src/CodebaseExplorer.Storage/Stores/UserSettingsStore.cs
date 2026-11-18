using System.Text.Json;
using CodebaseExplorer.Application.Settings;
using CodebaseExplorer.Storage.Persistence;
using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Stores;

/// <summary>
/// SQLite implementation of <see cref="IUserSettingsStore"/> (STORE-005). A value is stored as a JSON string
/// in <c>ValueJson</c>; a row whose JSON is not a string reads as missing.
/// </summary>
internal sealed class UserSettingsStore(IDbContextFactory<CodebaseExplorerDbContext> contextFactory, TimeProvider timeProvider) : IUserSettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.UserSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        return record is null ? null : ReadString(record.ValueJson);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.UserSettings.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (record is null)
        {
            record = new UserSettingRecord { Key = key };
            context.UserSettings.Add(record);
        }

        record.ValueJson = JsonSerializer.Serialize(value, StorageJsonContext.Default.String);
        record.UpdatedAt = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string? ReadString(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.String ? document.RootElement.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
