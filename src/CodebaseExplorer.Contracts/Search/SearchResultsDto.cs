using CodebaseExplorer.Contracts.Inspector;

namespace CodebaseExplorer.Contracts.Search;

/// <summary>Result of an element search (GRAPH-007, USER_WORKFLOWS.md WF-05).</summary>
/// <param name="Items">Best matches first, at most the requested number.</param>
/// <param name="TotalCount">Number of all matches.</param>
public sealed record SearchResultsDto(IReadOnlyList<SearchResultDto> Items, int TotalCount);

/// <summary>One match: the element with the graph node that shows it and the nodes to expand, plus display texts.</summary>
/// <param name="Element">Element reference as used by the inspector's <c>Show in graph</c>.</param>
/// <param name="KindText">Readable kind, e.g. <c>Class</c>, <c>Project</c>, <c>File module</c>.</param>
/// <param name="Container">Where the element lives (project and module), for disambiguation.</param>
public sealed record SearchResultDto(ElementReferenceDto Element, string KindText, string? Container);
