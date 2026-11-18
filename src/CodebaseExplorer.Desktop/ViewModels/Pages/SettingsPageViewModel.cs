using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Desktop.Services;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// Settings page: theme (persisted, STORE-005), the local data directory (open it in the file manager, disk usage,
/// compact the database) and the log folder.
/// </summary>
public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly IThemeService _themeService;
    private readonly ShellStatus _shell;
    private readonly ILogger<SettingsPageViewModel> _logger;
    private readonly IStorageMaintenance _storage;
    private readonly IFolderLauncher _folders;
    private readonly bool _initialized;

    public SettingsPageViewModel(
        IThemeService themeService,
        IApplicationPaths paths,
        IStorageMaintenance storage,
        IFolderLauncher folders,
        ShellStatus shell,
        ILogger<SettingsPageViewModel> logger)
    {
        _themeService = themeService;
        _storage = storage;
        _folders = folders;
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

    /// <summary>Refreshes the disk usage each time the page is shown (imports and removals change it).</summary>
    public override Task ActivateAsync()
    {
        RefreshUsage();
        return Task.CompletedTask;
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
