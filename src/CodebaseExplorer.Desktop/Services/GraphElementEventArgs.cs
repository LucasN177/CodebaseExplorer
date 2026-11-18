namespace CodebaseExplorer.Desktop.Services;

/// <summary>Event data for a request concerning one graph element (e.g. <c>node/openInspector</c>).</summary>
public sealed class GraphElementEventArgs(string elementId) : EventArgs
{
    public string ElementId { get; } = elementId;
}
