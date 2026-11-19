using System.Globalization;
using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.Settings;

/// <summary>
/// Reads and writes <see cref="AnalysisSettings"/> (keys <c>analysis.*</c> and <c>git.*</c>, DATABASE_SCHEMA.md §4.17).
/// Missing or out-of-range values fall back to the defaults individually; invalid folder names are dropped.
/// </summary>
public sealed class AnalysisSettingsStore(IUserSettingsStore store)
{
    public async Task<AnalysisSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var defaults = AnalysisSettings.Default;
        var fileSize = await IntAsync(UserSettingKeys.AnalysisMaxFileSizeKilobytes, cancellationToken);
        var files = await IntAsync(UserSettingKeys.AnalysisMaxFiles, cancellationToken);
        var timeout = await IntAsync(UserSettingKeys.AnalysisParseTimeoutSeconds, cancellationToken);
        var folders = await store.GetAsync(UserSettingKeys.AnalysisExcludedFolders, cancellationToken);
        var commits = await IntAsync(UserSettingKeys.GitHistoryMaxCommits, cancellationToken);
        var days = await IntAsync(UserSettingKeys.GitHistoryDays, cancellationToken);

        return new AnalysisSettings
        {
            MaxFileSizeKilobytes = fileSize is >= AnalysisSettings.MinFileSizeKilobytes and <= AnalysisSettings.MaxFileSizeKilobytesLimit
                ? fileSize.Value : defaults.MaxFileSizeKilobytes,
            MaxFiles = files is >= AnalysisSettings.MinFiles and <= AnalysisSettings.MaxFilesLimit ? files.Value : defaults.MaxFiles,
            ParseTimeoutSeconds = timeout is >= AnalysisSettings.MinParseSeconds and <= AnalysisSettings.MaxParseSeconds
                ? timeout.Value : defaults.ParseTimeoutSeconds,
            ExcludedFolders = [.. AnalysisSettings.ParseFolders(folders).Where(AnalysisSettings.IsFolderName).Take(AnalysisSettings.MaxExcludedFolders)],
            HistoryMaxCommits = commits is >= AnalysisSettings.MinHistoryCommits and <= AnalysisSettings.MaxHistoryCommits
                ? commits.Value : defaults.HistoryMaxCommits,
            HistoryDays = days is >= 0 and <= AnalysisSettings.MaxHistoryDays ? days.Value : defaults.HistoryDays,
        };
    }

    /// <summary>Validates and stores the settings; they apply to the next import.</summary>
    public async Task<Result<AnalysisSettings>> SaveAsync(AnalysisSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Validate() is { } error)
        {
            return error;
        }

        await SetAsync(UserSettingKeys.AnalysisMaxFileSizeKilobytes, settings.MaxFileSizeKilobytes, cancellationToken);
        await SetAsync(UserSettingKeys.AnalysisMaxFiles, settings.MaxFiles, cancellationToken);
        await SetAsync(UserSettingKeys.AnalysisParseTimeoutSeconds, settings.ParseTimeoutSeconds, cancellationToken);
        await store.SetAsync(UserSettingKeys.AnalysisExcludedFolders, string.Join('\n', settings.ExcludedFolders), cancellationToken);
        await SetAsync(UserSettingKeys.GitHistoryMaxCommits, settings.HistoryMaxCommits, cancellationToken);
        await SetAsync(UserSettingKeys.GitHistoryDays, settings.HistoryDays, cancellationToken);
        return settings;
    }

    private async Task<int?> IntAsync(string key, CancellationToken cancellationToken) =>
        int.TryParse(await store.GetAsync(key, cancellationToken), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private Task SetAsync(string key, int value, CancellationToken cancellationToken) =>
        store.SetAsync(key, value.ToString(CultureInfo.InvariantCulture), cancellationToken);
}
