namespace CodebaseExplorer.Desktop.Services;

/// <summary>Current selection in the graph web view (element ids of the loaded graph).</summary>
public sealed record GraphSelection(IReadOnlyList<string> NodeIds, IReadOnlyList<string> EdgeIds)
{
    public static GraphSelection Empty { get; } = new([], []);

    public bool IsEmpty => NodeIds.Count == 0 && EdgeIds.Count == 0;
}
