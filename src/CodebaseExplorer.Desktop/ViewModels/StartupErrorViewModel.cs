namespace CodebaseExplorer.Desktop.ViewModels;

/// <summary>
/// Shown instead of the main window when the local database cannot be prepared at startup
/// (STORE-003, DATABASE_SCHEMA.md §5). The database file is never deleted in that case.
/// </summary>
/// <param name="message">What went wrong.</param>
/// <param name="databaseFile">The database file that could not be prepared.</param>
/// <param name="backupDirectory">Where backups taken before migrations are kept.</param>
public sealed class StartupErrorViewModel(string message, string databaseFile, string backupDirectory) : ViewModelBase
{
    public string Message { get; } = message;

    public string DatabaseFile { get; } = databaseFile;

    public string BackupDirectory { get; } = backupDirectory;
}
