using System.ComponentModel;
using CodebaseExplorer.Contracts.Graph;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>Theme of the graph web view (<c>view/theme</c>).</summary>
public enum GraphThemeMode
{
    Light,
    Dark,
}

/// <summary>Current selection in the graph web view (element ids of the loaded graph).</summary>
public sealed record GraphSelection(IReadOnlyList<string> NodeIds, IReadOnlyList<string> EdgeIds)
{
    public static GraphSelection Empty { get; } = new([], []);

    public bool IsEmpty => NodeIds.Count == 0 && EdgeIds.Count == 0;
}

/// <summary>Event data for a request concerning one graph element (e.g. <c>node/openInspector</c>).</summary>
public sealed class GraphElementEventArgs(string elementId) : EventArgs
{
    public string ElementId { get; } = elementId;
}

/// <summary>
/// What ViewModels see of the graph web view (GRAPH_ENGINE.md §1, "Graph host"). ViewModels never
/// touch the WebView; they send Contracts DTOs and observe selection through this abstraction.
/// <see cref="IsReady"/> and <see cref="Selection"/> raise <see cref="INotifyPropertyChanged.PropertyChanged"/>.
/// </summary>
public interface IGraphViewHost : INotifyPropertyChanged
{
    /// <summary>True while the page is loaded and has reported <c>ready</c>.</summary>
    bool IsReady { get; }

    /// <summary>The graph last passed to <see cref="ShowGraphAsync"/>, or <see langword="null"/>.</summary>
    GraphViewDto? CurrentGraph { get; }

    /// <summary>Selection reported by the web view; ids always belong to <see cref="CurrentGraph"/>.</summary>
    GraphSelection Selection { get; }

    /// <summary>Raised when the user asks to inspect a node (double click in the web view).</summary>
    event EventHandler<GraphElementEventArgs>? InspectorRequested;

    /// <summary>
    /// Replaces the displayed graph. If the page is not ready yet, the graph is shown as soon as it is.
    /// Returns <see langword="false"/> if the web view rejected the message.
    /// </summary>
    Task<bool> ShowGraphAsync(GraphViewDto view, CancellationToken cancellationToken = default);

    /// <summary>Selects and centers an element of the current graph. Returns <see langword="false"/> if not delivered.</summary>
    Task<bool> FocusAsync(string elementId, CancellationToken cancellationToken = default);

    /// <summary>Applies a theme; remembered and re-applied after a page reload.</summary>
    Task<bool> SetThemeAsync(GraphThemeMode mode, CancellationToken cancellationToken = default);
}
