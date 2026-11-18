namespace CodebaseExplorer.Desktop.Services;

/// <summary>Native folder picker. Keeps Avalonia's storage provider out of ViewModels.</summary>
public interface IFolderPicker
{
    /// <summary>
    /// Lets the user choose one folder. Returns its absolute local path, or <see langword="null"/> if the
    /// user cancelled or chose a location that is not a local folder.
    /// </summary>
    Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken);
}
