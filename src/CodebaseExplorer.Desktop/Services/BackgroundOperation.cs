using CommunityToolkit.Mvvm.ComponentModel;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>A running background operation shown in the status bar; dispose it when the operation ends.</summary>
public sealed partial class BackgroundOperation : ObservableObject, IDisposable
{
    private readonly Action<BackgroundOperation> _onDisposed;
    private bool _disposed;

    internal BackgroundOperation(string description, Action<BackgroundOperation> onDisposed)
    {
        Description = description;
        _onDisposed = onDisposed;
    }

    /// <summary>What runs, e.g. <c>Analyzing dotnet-layered</c>.</summary>
    public string Description { get; }

    /// <summary>Current step, e.g. <c>Detecting projects 2 / 4</c>.</summary>
    [ObservableProperty]
    public partial string? Detail { get; set; }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _onDisposed(this);
        }
    }
}
