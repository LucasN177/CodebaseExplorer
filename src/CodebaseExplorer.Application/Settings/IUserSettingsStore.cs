namespace CodebaseExplorer.Application.Settings;

/// <summary>
/// Persistent user settings (table <c>UserSettings</c>, DATABASE_SCHEMA.md §4.17; STORE-005). Values are
/// small strings under the keys of <see cref="UserSettingKeys"/>. Secrets are never stored here (OPD-03).
/// </summary>
public interface IUserSettingsStore
{
    /// <summary>The stored value, or <see langword="null"/> if the key was never set or its stored value is unreadable.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Stores (inserts or replaces) the value of a key.</summary>
    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}
