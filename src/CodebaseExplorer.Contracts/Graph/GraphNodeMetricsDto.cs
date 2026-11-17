namespace CodebaseExplorer.Contracts.Graph;

/// <summary>Node metrics.</summary>
/// <param name="FanIn">Number of distinct visible nodes with an edge to this node.</param>
/// <param name="FanOut">Number of distinct visible nodes this node has an edge to.</param>
/// <param name="Size">Size measure of the element (for projects: number of owned files).</param>
/// <param name="ChangeCount">Git change count, once Git data exists (M5).</param>
public sealed record GraphNodeMetricsDto(int FanIn, int FanOut, int Size, int? ChangeCount = null);
