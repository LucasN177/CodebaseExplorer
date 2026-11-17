namespace CodebaseExplorer.Domain.Projects;

/// <summary>
/// Well-known keys in <see cref="CodeProject.Properties"/> that carry model conventions shared by all
/// modules (DOMAIN_MODEL.md §4.4).
/// </summary>
public static class ProjectPropertyKeys
{
    /// <summary>
    /// Set to <see cref="ResolutionMissing"/> on placeholder projects that stand for a referenced but
    /// missing project file, so that unresolved references stay visible in the model.
    /// </summary>
    public const string Resolution = "resolution";

    public const string ResolutionMissing = "missing";

    /// <summary>Repository-relative path of the missing project file of a placeholder.</summary>
    public const string ReferencedPath = "referencedPath";

    /// <summary>True for placeholder projects of missing project files.</summary>
    public static bool IsPlaceholder(this CodeProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Properties.TryGetValue(Resolution, out var value) && value == ResolutionMissing;
    }
}
