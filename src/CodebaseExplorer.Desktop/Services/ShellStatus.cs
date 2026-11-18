using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>Severity of a shell notification.</summary>
public enum NotificationSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>A message in the shell's notification area (e.g. a failed background operation).</summary>
public sealed record ShellNotification(NotificationSeverity Severity, string Message, DateTimeOffset Time);

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

/// <summary>
/// Shell state shared by all pages (UI-001): running background operations for the status bar and
/// busy indicator, and notifications (errors of background work that may not be visible on the
/// current page). Used from the UI thread.
/// </summary>
public sealed partial class ShellStatus : ObservableObject
{
    /// <summary>At most this many notifications are kept; older ones are dropped.</summary>
    public const int MaxNotifications = 5;

    private const string IdleText = "Ready";

    private readonly TimeProvider _timeProvider;
    private readonly List<BackgroundOperation> _operations = [];

    public ShellStatus(TimeProvider timeProvider) => _timeProvider = timeProvider;

    /// <summary>True while at least one background operation runs.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Status bar text: the most recent operation and its step, or <c>Ready</c>.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = IdleText;

    /// <summary>Newest first.</summary>
    public ObservableCollection<ShellNotification> Notifications { get; } = [];

    /// <summary>Registers a running operation; dispose the result when it has finished, failed or was cancelled.</summary>
    public BackgroundOperation BeginOperation(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var operation = new BackgroundOperation(description, EndOperation);
        operation.PropertyChanged += (_, _) => Refresh();
        _operations.Add(operation);
        Refresh();
        return operation;
    }

    public void Notify(NotificationSeverity severity, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Notifications.Insert(0, new ShellNotification(severity, message, _timeProvider.GetLocalNow()));
        while (Notifications.Count > MaxNotifications)
        {
            Notifications.RemoveAt(Notifications.Count - 1);
        }
    }

    public void Dismiss(ShellNotification notification) => Notifications.Remove(notification);

    private void EndOperation(BackgroundOperation operation)
    {
        _operations.Remove(operation);
        Refresh();
    }

    private void Refresh()
    {
        IsBusy = _operations.Count > 0;
        StatusText = _operations.Count == 0
            ? IdleText
            : _operations[^1] is { Detail: { Length: > 0 } detail } latest
                ? $"{latest.Description}: {detail}"
                : _operations[^1].Description;
    }
}
