using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>Access to MSBuild files of the repository for static project analysis.</summary>
internal interface IMsBuildFileSource
{
    /// <summary>True if the scan contains the file (used to find <c>Directory.*.props</c> and referenced projects).</summary>
    bool Exists(RelativePath path);

    /// <summary>Loads the file's text, or returns an error message (too large, unreadable, …).</summary>
    Task<(string? Content, string? Error)> LoadAsync(RelativePath path, CancellationToken cancellationToken);
}
