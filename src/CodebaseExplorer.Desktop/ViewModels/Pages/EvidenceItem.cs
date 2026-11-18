namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>One evidence record in the side panel, e.g. "project-reference · confirmed" at "src/A/A.csproj:12".</summary>
/// <param name="Title">Relationship kind and evidence level (plus endpoints if they differ from the edge's nodes).</param>
/// <param name="Location">File and line, or <c>no file location</c>.</param>
/// <param name="Source">Analyzer and rule that produced the evidence.</param>
public sealed record EvidenceItem(string Title, string Location, string Source);
