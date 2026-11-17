namespace CodebaseExplorer.Contracts.Graph;

/// <summary>One step of the navigation path shown above the graph.</summary>
/// <param name="Id">Element id, or the repository id for the first step.</param>
/// <param name="Label">Display text.</param>
/// <param name="Level">One of <see cref="GraphLevels"/>.</param>
public sealed record BreadcrumbDto(string Id, string Label, string Level);
