using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Projects;

/// <summary>
/// Membership of a .NET project in a solution file. A grouping, not a dependency
/// (DOMAIN_MODEL.md §4.4).
/// </summary>
/// <param name="SolutionFileId">Id of the <c>.sln</c>/<c>.slnx</c> file.</param>
/// <param name="SolutionFolderPath">Solution folder path such as <c>/src/</c>, if any.</param>
public sealed record SolutionMembership(ElementId SolutionFileId, string? SolutionFolderPath = null);
