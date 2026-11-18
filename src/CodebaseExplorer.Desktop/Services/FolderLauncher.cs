using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>Opens folders through the main window's platform launcher (no process is started by the app itself).</summary>
internal sealed class FolderLauncher : IFolderLauncher
{
    public async Task<bool> OpenAsync(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Directory.Exists(folder)
            || Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow.Launcher: { } launcher })
        {
            return false;
        }

        return await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }
}
