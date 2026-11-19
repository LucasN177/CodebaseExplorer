using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Application.Settings;
using CodebaseExplorer.Desktop.Services;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// Settings page: theme (persisted, STORE-005), the optional AI assistant (AI-001/AI-002), the local data directory (open
/// it in the file manager, disk usage, compact the database) and the log folder.
/// </summary>
public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly IThemeService _themeService;
    private readonly ShellStatus _shell;
    private readonly ILogger<SettingsPageViewModel> _logger;
    private readonly IStorageMaintenance _storage;
    private readonly IFolderLauncher _folders;
    private readonly bool _initialized;

    private readonly AiSettingsStore _aiSettings;
    private readonly IAiAssistant _assistant;
    private readonly AnalysisSettingsStore _analysisSettings;
    private readonly PrepareEditorLaunch _editor;
    private readonly RemoveAllRepositories _removeAll;
    private readonly RepositorySession _session;
    private AiSettings _storedAi = AiSettings.Default;

    public SettingsPageViewModel(
        IThemeService themeService,
        IApplicationPaths paths,
        IStorageMaintenance storage,
        IFolderLauncher folders,
        AiSettingsStore aiSettings,
        IAiAssistant assistant,
        AnalysisSettingsStore analysisSettings,
        PrepareEditorLaunch editor,
        RemoveAllRepositories removeAll,
        RepositorySession session,
        ShellStatus shell,
        ILogger<SettingsPageViewModel> logger)
    {
        _themeService = themeService;
        _storage = storage;
        _folders = folders;
        _aiSettings = aiSettings;
        _assistant = assistant;
        _analysisSettings = analysisSettings;
        _editor = editor;
        _removeAll = removeAll;
        _session = session;
        _shell = shell;
        _logger = logger;
        SelectedTheme = themeService.CurrentTheme;
        DataDirectory = paths.DataDirectory;
        LogsDirectory = paths.LogsDirectory;
        _initialized = true;
    }

    public override string Title => "Settings";

    public override string Glyph => "⚙";

    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    [ObservableProperty]
    public partial AppTheme SelectedTheme { get; set; }

    /// <summary>Where analysis results are stored locally (local-first).</summary>
    public string DataDirectory { get; }

    /// <summary>Where the application log files are written (CORE-007).</summary>
    public string LogsDirectory { get; }

    /// <summary>E.g. <c>Database 12.4 MB · backups 3.1 MB</c>.</summary>
    [ObservableProperty]
    public partial string? StorageUsageText { get; private set; }

    /// <summary>Result or error of the last compaction.</summary>
    [ObservableProperty]
    public partial string? CompactionMessage { get; private set; }

    /// <summary>The AI assistant may be used (AI-001); off by default.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiRemoteWarning))]
    public partial bool AiEnabled { get; set; }

    /// <summary>Base URL of the Ollama server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiRemoteWarning))]
    public partial string AiEndpoint { get; set; } = AiSettings.DefaultEndpoint;

    /// <summary>Model name at the provider, e.g. <c>qwen2.5-coder:7b</c>.</summary>
    [ObservableProperty]
    public partial string? AiModel { get; set; }

    /// <summary>Models reported by the provider (filled by <see cref="TestAiConnectionCommand"/>).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> AiModels { get; private set; } = [];

    [ObservableProperty]
    public partial decimal AiMaxContextTokens { get; set; } = AiSettings.DefaultMaxContextTokens;

    [ObservableProperty]
    public partial decimal AiTemperature { get; set; } = (decimal)AiSettings.DefaultTemperature;

    /// <summary>Result of the last save or connection test.</summary>
    [ObservableProperty]
    public partial string? AiStatusMessage { get; private set; }

    public decimal AiMinContextTokens => AiSettings.MinContextTokens;

    public decimal AiMaxContextTokensLimit => AiSettings.MaxContextTokensLimit;

    /// <summary>Shown when the endpoint is not on this machine: repository context then needs consent per repository.</summary>
    public string? AiRemoteWarning => AiEnabled && Current().IsRemoteEndpoint
        ? "This endpoint is not on this machine (or not a valid URL). Code and analysis facts of a repository are sent there only after you allow it for that repository."
        : null;

    // ---- Analysis and Git history (UI-009) ----

    [ObservableProperty]
    public partial decimal MaxFileSizeKilobytes { get; set; } = AnalysisSettings.Default.MaxFileSizeKilobytes;

    [ObservableProperty]
    public partial decimal MaxFiles { get; set; } = AnalysisSettings.Default.MaxFiles;

    [ObservableProperty]
    public partial decimal ParseTimeoutSeconds { get; set; } = AnalysisSettings.Default.ParseTimeoutSeconds;

    /// <summary>Additional excluded folder names, one per line.</summary>
    [ObservableProperty]
    public partial string? ExcludedFolders { get; set; }

    [ObservableProperty]
    public partial decimal HistoryMaxCommits { get; set; } = AnalysisSettings.Default.HistoryMaxCommits;

    /// <summary>0 = no date limit.</summary>
    [ObservableProperty]
    public partial decimal HistoryDays { get; set; } = AnalysisSettings.Default.HistoryDays;

    [ObservableProperty]
    public partial string? AnalysisStatusMessage { get; private set; }

    public string BuiltInExcludes => string.Join(", ", Application.Analysis.AnalysisLimits.DefaultExcludedDirectoryNames);

    public decimal MinFileSizeKilobytes => AnalysisSettings.MinFileSizeKilobytes;

    public decimal MaxFileSizeKilobytesLimit => AnalysisSettings.MaxFileSizeKilobytesLimit;

    public decimal MinFiles => AnalysisSettings.MinFiles;

    public decimal MaxFilesLimit => AnalysisSettings.MaxFilesLimit;

    public decimal MinParseSeconds => AnalysisSettings.MinParseSeconds;

    public decimal MaxParseSeconds => AnalysisSettings.MaxParseSeconds;

    public decimal MinHistoryCommits => AnalysisSettings.MinHistoryCommits;

    public decimal MaxHistoryCommits => AnalysisSettings.MaxHistoryCommits;

    public decimal MaxHistoryDays => AnalysisSettings.MaxHistoryDays;

    [RelayCommand]
    private async Task SaveAnalysisSettingsAsync()
    {
        try
        {
            var result = await _analysisSettings.SaveAsync(new AnalysisSettings
            {
                MaxFileSizeKilobytes = (int)MaxFileSizeKilobytes,
                MaxFiles = (int)MaxFiles,
                ParseTimeoutSeconds = (int)ParseTimeoutSeconds,
                ExcludedFolders = AnalysisSettings.ParseFolders(ExcludedFolders),
                HistoryMaxCommits = (int)HistoryMaxCommits,
                HistoryDays = (int)HistoryDays,
            }, CancellationToken.None);
            AnalysisStatusMessage = result.TryGetValue(out var saved, out var error)
                ? "Saved. The settings apply to the next import or re-analysis."
                : error.Message;
            if (saved is not null)
            {
                ShowAnalysis(saved);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Saving the analysis settings failed");
            AnalysisStatusMessage = "The analysis settings could not be saved. Details are in the application log.";
        }
    }

    [RelayCommand]
    private Task ResetAnalysisSettingsAsync()
    {
        ShowAnalysis(AnalysisSettings.Default);
        return SaveAnalysisSettingsAsync();
    }

    private void ShowAnalysis(AnalysisSettings settings)
    {
        MaxFileSizeKilobytes = settings.MaxFileSizeKilobytes;
        MaxFiles = settings.MaxFiles;
        ParseTimeoutSeconds = settings.ParseTimeoutSeconds;
        ExcludedFolders = string.Join(Environment.NewLine, settings.ExcludedFolders);
        HistoryMaxCommits = settings.HistoryMaxCommits;
        HistoryDays = settings.HistoryDays;
    }

    // ---- External editor (UI-009) ----

    /// <summary>E.g. <c>code --goto "{path}:{line}"</c>; empty = no editor.</summary>
    [ObservableProperty]
    public partial string? EditorCommand { get; set; }

    [ObservableProperty]
    public partial string? EditorStatusMessage { get; private set; }

    /// <summary>Examples shown on the page.</summary>
    public IReadOnlyList<string> EditorExamples { get; } =
    [
        "code --goto \"{path}:{line}\"",
        "rider --line {line} \"{path}\"",
        "subl \"{path}:{line}\"",
        "notepad++ -n{line} \"{path}\"",
    ];

    [RelayCommand]
    private async Task SaveEditorCommandAsync()
    {
        try
        {
            var result = await _editor.SaveTemplateAsync(EditorCommand, CancellationToken.None);
            EditorStatusMessage = result.IsSuccess
                ? string.IsNullOrWhiteSpace(EditorCommand) ? "Saved. No external editor is configured." : "Saved."
                : result.Error.Message;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Saving the editor command failed");
            EditorStatusMessage = "The editor command could not be saved. Details are in the application log.";
        }
    }

    // ---- Delete all data (UI-009) ----

    /// <summary>Must be ticked before <see cref="DeleteAllDataCommand"/> runs (the deletion cannot be undone).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteAllDataCommand))]
    public partial bool ConfirmDeleteAll { get; set; }

    [ObservableProperty]
    public partial string? DeleteAllMessage { get; private set; }

    /// <summary>Deletes every stored repository (with runs, models, Git and AI data, app-managed clones) and compacts.</summary>
    [RelayCommand(CanExecute = nameof(ConfirmDeleteAll))]
    private async Task DeleteAllDataAsync()
    {
        DeleteAllMessage = "Deleting…";
        using var operation = _shell.BeginOperation("Deleting all stored data");
        try
        {
            var result = await _removeAll.ExecuteAsync(CancellationToken.None);
            _session.Clear();
            var summary = result.Value;
            DeleteAllMessage = (summary.Problems.Count == 0
                    ? $"Deleted {summary.Removed} repositories and their data."
                    : $"Deleted {summary.Removed} repositories; problems: {string.Join("; ", summary.Problems)}")
                + (summary.Compaction is { } compaction ? $" The database now uses {Size(compaction.BytesAfter)}." : " The database could not be compacted.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Deleting all data failed");
            DeleteAllMessage = "Deleting the data failed. Details are in the application log.";
        }
        finally
        {
            ConfirmDeleteAll = false;
        }

        RefreshUsage();
    }

    /// <summary>Refreshes the disk usage and all settings each time the page is shown.</summary>
    public override async Task ActivateAsync()
    {
        RefreshUsage();
        try
        {
            ShowAnalysis(await _analysisSettings.LoadAsync(CancellationToken.None));
            EditorCommand = await _editor.GetTemplateAsync(CancellationToken.None);
            ShowAi(await _aiSettings.LoadAsync(CancellationToken.None));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Loading the AI settings failed");
            AiStatusMessage = "The AI settings could not be loaded. Details are in the application log.";
        }
    }

    /// <summary>Validates and stores the AI settings.</summary>
    [RelayCommand]
    private async Task SaveAiSettingsAsync()
    {
        try
        {
            var result = await _aiSettings.SaveAsync(Current(), CancellationToken.None);
            if (result.TryGetValue(out var saved, out var error))
            {
                var consentsCleared = _storedAi.RemoteConsents.Count > 0 && saved.RemoteConsents.Count == 0;
                ShowAi(saved);
                AiStatusMessage = !saved.Enabled
                    ? "Saved. The AI assistant is off; nothing is sent anywhere."
                    : consentsCleared
                        ? "Saved. The endpoint changed, so all repository consents were withdrawn."
                        : "Saved.";
            }
            else
            {
                AiStatusMessage = error.Message;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Saving the AI settings failed");
            AiStatusMessage = "The AI settings could not be saved. Details are in the application log.";
        }
    }

    /// <summary>Checks the endpoint and lists its models; sends no repository content.</summary>
    [RelayCommand]
    private async Task TestAiConnectionAsync()
    {
        AiStatusMessage = "Connecting…";
        using var operation = _shell.BeginOperation("Checking the AI provider");
        try
        {
            var result = await _assistant.CheckProviderAsync(Current(), CancellationToken.None);
            if (result.TryGetValue(out var status, out var error))
            {
                AiModels = status.Models;
                AiStatusMessage = status.Models.Count == 0
                    ? $"Connected to {status.Endpoint}, but it offers no models. Download one first (e.g. `ollama pull qwen2.5-coder:7b`)."
                    : $"Connected to {status.Endpoint}{(status.Version is null ? string.Empty : $" (version {status.Version})")}: {status.Models.Count} model(s) available.";
                if (AiModel is null && status.Models.Count > 0)
                {
                    AiModel = status.Models[0];
                }
            }
            else
            {
                AiStatusMessage = error.Message;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Checking the AI provider failed unexpectedly");
            AiStatusMessage = "The AI provider could not be checked. Details are in the application log.";
        }
    }

    /// <summary>The settings as currently entered on the page (not yet saved).</summary>
    private AiSettings Current() => _storedAi with
    {
        Enabled = AiEnabled,
        ProviderId = AiSettings.OllamaProviderId,
        Endpoint = AiEndpoint?.Trim() ?? string.Empty,
        ModelId = string.IsNullOrWhiteSpace(AiModel) ? null : AiModel.Trim(),
        MaxContextTokens = (int)AiMaxContextTokens,
        Temperature = (double)AiTemperature,
    };

    private void ShowAi(AiSettings settings)
    {
        _storedAi = settings;
        AiEnabled = settings.Enabled;
        AiEndpoint = settings.Endpoint;
        AiModel = settings.ModelId;
        AiMaxContextTokens = settings.MaxContextTokens;
        AiTemperature = (decimal)settings.Temperature;
    }

    /// <summary>Opens the data directory in the file manager.</summary>
    [RelayCommand]
    private Task OpenDataDirectoryAsync() => OpenFolderAsync(DataDirectory, "data directory");

    /// <summary>Opens the log folder in the file manager.</summary>
    [RelayCommand]
    private Task OpenLogsDirectoryAsync() => OpenFolderAsync(LogsDirectory, "log folder");

    /// <summary>Returns the space freed by removed repositories and replaced runs to the file system.</summary>
    [RelayCommand]
    private async Task CompactAsync()
    {
        CompactionMessage = "Compacting…";
        using var operation = _shell.BeginOperation("Compacting the database");
        try
        {
            var result = await _storage.CompactAsync(CancellationToken.None);
            CompactionMessage = result.TryGetValue(out var compaction, out var error)
                ? $"Compacted: {Size(compaction.BytesBefore)} → {Size(compaction.BytesAfter)} ({Size(compaction.BytesFreed)} freed)."
                : error.Message;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Compacting the database failed unexpectedly");
            CompactionMessage = "The database could not be compacted. Details are in the application log.";
        }

        RefreshUsage();
    }

    private async Task OpenFolderAsync(string folder, string name)
    {
        try
        {
            if (await _folders.OpenAsync(folder))
            {
                return;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Opening the {FolderName} failed", name);
        }

        _shell.Notify(NotificationSeverity.Error, $"The {name} could not be opened. Its path can be copied from the Settings page.");
    }

    private void RefreshUsage()
    {
        try
        {
            var usage = _storage.GetUsage();
            StorageUsageText = $"Database {Size(usage.DatabaseBytes)} · backups {Size(usage.BackupBytes)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The disk usage of the data directory could not be determined");
            StorageUsageText = "The disk usage could not be determined.";
        }
    }

    /// <summary>Human-readable size (binary units, invariant culture).</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.0 'KB'", CultureInfo.InvariantCulture),
        < 1024L * 1024 * 1024 => (bytes / (1024d * 1024)).ToString("0.0 'MB'", CultureInfo.InvariantCulture),
        _ => (bytes / (1024d * 1024 * 1024)).ToString("0.00 'GB'", CultureInfo.InvariantCulture),
    };

    // The initial value comes from the theme service; only user changes are applied and stored.
    partial void OnSelectedThemeChanged(AppTheme value)
    {
        if (_initialized)
        {
            _ = ChangeThemeAsync(value);
        }
    }

    private async Task ChangeThemeAsync(AppTheme theme)
    {
        try
        {
            await _themeService.ChangeAsync(theme, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The theme is applied already; only remembering it failed.
            _logger.LogError(exception, "Saving the theme failed");
            _shell.Notify(NotificationSeverity.Error, "The theme could not be saved and will be reset at the next start.");
        }
    }
}
