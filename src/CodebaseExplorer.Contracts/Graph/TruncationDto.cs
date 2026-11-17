namespace CodebaseExplorer.Contracts.Graph;

/// <summary>Describes nodes omitted from a view and how to see them.</summary>
public sealed record TruncationDto(int OmittedNodeCount, string Reason, IReadOnlyList<string> Suggestions);
