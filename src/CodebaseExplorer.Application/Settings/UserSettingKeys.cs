namespace CodebaseExplorer.Application.Settings;

/// <summary>Keys of the <see cref="IUserSettingsStore"/>; every key and its values are listed in DATABASE_SCHEMA.md §4.17.</summary>
public static class UserSettingKeys
{
    /// <summary>Theme choice: <c>system</c>, <c>light</c> or <c>dark</c>. Missing or unknown values mean <c>system</c>.</summary>
    public const string Theme = "appearance.theme";
}
