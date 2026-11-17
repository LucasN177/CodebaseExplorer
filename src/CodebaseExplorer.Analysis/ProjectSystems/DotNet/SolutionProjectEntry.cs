using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>A project listed in a solution file.</summary>
/// <param name="ProjectPath">Repository-relative path of the project file.</param>
/// <param name="SolutionFolder">Solution folder in <c>.slnx</c> form (<c>/src/</c>, <c>/src/sub/</c>); <see langword="null"/> at the top level.</param>
/// <param name="Line">1-based line of the entry in the solution file.</param>
/// <param name="Column">1-based column of the entry in the solution file.</param>
internal sealed record SolutionProjectEntry(RelativePath ProjectPath, string? SolutionFolder, int Line, int Column);
