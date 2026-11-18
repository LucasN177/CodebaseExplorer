namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>UserSettings</c> (§4.17).</summary>
internal sealed class UserSettingRecord
{
    public string Key { get; set; } = string.Empty;

    public string ValueJson { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
