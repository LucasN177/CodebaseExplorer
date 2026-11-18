using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>Folder picker based on the storage provider of the main window.</summary>
internal sealed class FolderPicker : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return null;
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }
}
