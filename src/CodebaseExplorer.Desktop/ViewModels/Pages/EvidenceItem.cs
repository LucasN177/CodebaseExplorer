namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>One evidence record in the side panel, e.g. "project-reference · confirmed" at "src/A/A.csproj:12".</summary>
/// <param name="Title">Relationship kind and evidence level (plus endpoints if they differ from the edge's nodes).</param>
/// <param name="Location">File and line, or <c>no file location</c>.</param>
/// <param name="Source">Analyzer and rule that produced the evidence.</param>
/// <param name="Path">Repository-relative file, if the evidence has a location (UI-007: source snippet).</param>
/// <param name="StartLine">First line of the evidence.</param>
/// <param name="EndLine">Last line of the evidence.</param>
public sealed record EvidenceItem(string Title, string Location, string Source, string? Path = null, int? StartLine = null, int? EndLine = null)
{
    /// <summary>True if a source snippet can be shown for this record.</summary>
    public bool HasSource => Path is not null && StartLine is not null;

    /// <summary>Accessible name of the list item.</summary>
    public override string ToString() => $"{Title}, {Location}";
}
