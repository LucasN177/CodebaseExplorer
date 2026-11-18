namespace CodebaseExplorer.Desktop.Services;

/// <summary>Opens a local folder in the system's file manager (Explorer, Finder, …). Keeps Avalonia's TopLevel out of ViewModels.</summary>
public interface IFolderLauncher
{
    /// <summary>Returns <see langword="false"/> if the folder does not exist or the system could not open it.</summary>
    Task<bool> OpenAsync(string folder);
}
