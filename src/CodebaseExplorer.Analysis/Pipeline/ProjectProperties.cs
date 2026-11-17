using CodebaseExplorer.Domain.Projects;

namespace CodebaseExplorer.Analysis.Pipeline;

/// <summary>
/// Well-known keys in <see cref="CodeProject.Properties"/> set by the analysis pipeline.
/// </summary>
internal static class ProjectProperties
{
    /// <summary>
    /// Set to <see cref="ResolutionMissing"/> on placeholder projects that stand for a referenced but
    /// missing project file, so that unresolved references stay visible in the model (ANALYSIS-004).
    /// </summary>
    public const string Resolution = "resolution";

    public const string ResolutionMissing = "missing";

    /// <summary>Repository-relative path of the missing project file.</summary>
    public const string ReferencedPath = "referencedPath";

    public static bool IsPlaceholder(this CodeProject project) =>
        project.Properties.TryGetValue(Resolution, out var value) && value == ResolutionMissing;
}
