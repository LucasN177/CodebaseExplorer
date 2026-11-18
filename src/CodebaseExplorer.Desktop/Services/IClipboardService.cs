namespace CodebaseExplorer.Desktop.Services;

/// <summary>Copies text to the system clipboard. Keeps Avalonia's TopLevel out of ViewModels.</summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}
