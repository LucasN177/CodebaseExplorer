using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>Clipboard of the main window.</summary>
internal sealed class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.Clipboard: { } clipboard })
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
