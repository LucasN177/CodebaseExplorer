using CodebaseExplorer.Application.Abstractions;

namespace CodebaseExplorer.Desktop.ViewModels;

/// <summary>
/// Shown instead of the main window when the app cannot start: the local database cannot be prepared
/// (STORE-003, DATABASE_SCHEMA.md §5; the database file is never deleted in that case) or startup failed
/// unexpectedly (CORE-007). Points to the log files.
/// </summary>
public sealed class StartupErrorViewModel : ViewModelBase
{
    private StartupErrorViewModel(string summary, string? message, IApplicationPaths paths)
    {
        Summary = summary;
        Message = message;
        DatabaseFile = paths.DatabaseFilePath;
        BackupDirectory = Path.Combine(paths.DataDirectory, "backups");
        LogsDirectory = paths.LogsDirectory;
    }

    /// <summary>What happened, in one sentence.</summary>
    public string Summary { get; }

    /// <summary>The error message, if there is one to show.</summary>
    public string? Message { get; }

    public string DatabaseFile { get; }

    /// <summary>Where backups taken before migrations are kept.</summary>
    public string BackupDirectory { get; }

    public string LogsDirectory { get; }

    public static StartupErrorViewModel DatabaseFailed(string message, IApplicationPaths paths) =>
        new("The local database could not be prepared. Your data has not been deleted or changed.", message, paths);

    public static StartupErrorViewModel Unexpected(IApplicationPaths paths) =>
        new("An unexpected error occurred during startup. Details are in the log files.", message: null, paths);
}
