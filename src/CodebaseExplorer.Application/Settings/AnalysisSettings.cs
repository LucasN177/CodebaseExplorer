using System.Globalization;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;

namespace CodebaseExplorer.Application.Settings;

/// <summary>
/// User-configurable analysis limits, additional excluded folders and the Git history window (UI-009,
/// ANALYSIS_ENGINE.md §5, GIT_INTEGRATION.md §2). Applied by the next import; the built-in excludes always apply.
/// </summary>
public sealed record AnalysisSettings
{
    public const int MinFileSizeKilobytes = 16;
    public const int MaxFileSizeKilobytesLimit = 64 * 1024;
    public const int MinFiles = 100;
    public const int MaxFilesLimit = 5_000_000;
    public const int MinParseSeconds = 1;
    public const int MaxParseSeconds = 120;
    public const int MaxExcludedFolders = 100;
    public const int MinHistoryCommits = 1;
    public const int MaxHistoryCommits = 1_000_000;
    public const int MaxHistoryDays = 36_500;

    public static AnalysisSettings Default { get; } = new();

    /// <summary>Files above this size are recorded but not parsed.</summary>
    public int MaxFileSizeKilobytes { get; init; } = (int)(AnalysisLimits.Default.MaxParseFileBytes / 1024);

    /// <summary>Scanning stops after this many files.</summary>
    public int MaxFiles { get; init; } = AnalysisLimits.Default.MaxFiles;

    /// <summary>Time budget for parsing one file.</summary>
    public int ParseTimeoutSeconds { get; init; } = (int)AnalysisLimits.Default.MaxParseDuration.TotalSeconds;

    /// <summary>Folder names never descended into, in addition to the built-in ones (e.g. <c>generated</c>, <c>vendor</c>).</summary>
    public IReadOnlyList<string> ExcludedFolders { get; init; } = [];

    /// <summary>Git activity: at most this many commits.</summary>
    public int HistoryMaxCommits { get; init; } = GitHistoryWindow.DefaultMaxCommits;

    /// <summary>Git activity: commits of the last this many days; 0 means no date limit.</summary>
    public int HistoryDays { get; init; } = (int)GitHistoryWindow.DefaultPeriod.TotalDays;

    /// <summary>The limits for an analysis run: these settings applied to <see cref="AnalysisLimits.Default"/>.</summary>
    public AnalysisLimits ToLimits() => AnalysisLimits.Default with
    {
        MaxParseFileBytes = MaxFileSizeKilobytes * 1024L,
        MaxFiles = MaxFiles,
        MaxParseDuration = TimeSpan.FromSeconds(ParseTimeoutSeconds),
        ExcludedDirectoryNames = [.. AnalysisLimits.DefaultExcludedDirectoryNames
            .Concat(ExcludedFolders)
            .Distinct(StringComparer.OrdinalIgnoreCase)],
    };

    /// <summary>The Git history window for an import that starts at <paramref name="now"/>.</summary>
    public GitHistoryWindow ToHistoryWindow(DateTimeOffset now) =>
        new(HistoryMaxCommits, HistoryDays == 0 ? null : now - TimeSpan.FromDays(HistoryDays));

    /// <summary>Checks ranges and folder names; null if everything is fine.</summary>
    public ApplicationError? Validate()
    {
        if (MaxFileSizeKilobytes is < MinFileSizeKilobytes or > MaxFileSizeKilobytesLimit)
        {
            return Invalid($"The maximum file size must be between {MinFileSizeKilobytes} KB and {MaxFileSizeKilobytesLimit / 1024} MB.");
        }

        if (MaxFiles is < MinFiles or > MaxFilesLimit)
        {
            return Invalid($"The maximum number of files must be between {MinFiles} and {MaxFilesLimit}.");
        }

        if (ParseTimeoutSeconds is < MinParseSeconds or > MaxParseSeconds)
        {
            return Invalid($"The parse time limit must be between {MinParseSeconds} and {MaxParseSeconds} seconds.");
        }

        if (HistoryMaxCommits is < MinHistoryCommits or > MaxHistoryCommits)
        {
            return Invalid($"The number of commits must be between {MinHistoryCommits} and {MaxHistoryCommits}.");
        }

        if (HistoryDays is < 0 or > MaxHistoryDays)
        {
            return Invalid($"The history period must be between 0 (no limit) and {MaxHistoryDays} days.");
        }

        if (ExcludedFolders.Count > MaxExcludedFolders)
        {
            return Invalid($"At most {MaxExcludedFolders} excluded folders are allowed.");
        }

        return ExcludedFolders.FirstOrDefault(name => !IsFolderName(name)) is { } bad
            ? Invalid($"'{bad}' is not a folder name. Enter names only (e.g. vendor), no paths, wildcards or '..'.")
            : null;
    }

    /// <summary>A single folder name: no separators, wildcards, dots-only names or control characters, at most 128 characters.</summary>
    public static bool IsFolderName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 128
        && name.Trim('.').Length > 0
        && name.IndexOfAny(['/', '\\', '*', '?', ':', '"', '<', '>', '|']) < 0
        && !name.Any(char.IsControl)
        && name == name.Trim();

    /// <summary>Splits user input (one name per line, or separated by commas/semicolons) into distinct names.</summary>
    public static IReadOnlyList<string> ParseFolders(string? text) =>
        [.. (text ?? string.Empty)
            .Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static ApplicationError Invalid(string message) =>
        ApplicationError.Validation(SettingsErrorCodes.Invalid, string.Create(CultureInfo.InvariantCulture, $"{message}"));
}

/// <summary>Error codes of the settings use cases.</summary>
public static class SettingsErrorCodes
{
    /// <summary>A setting is outside its allowed range or malformed.</summary>
    public const string Invalid = "settings.invalid";
}
