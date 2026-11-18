using CodebaseExplorer.Domain.Projects;

namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>Projects</c> (§4.5).</summary>
internal sealed class ProjectRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NaturalKey { get; set; } = string.Empty;

    public ProjectKind Kind { get; set; }

    public Ecosystem Ecosystem { get; set; }

    public string PrimaryLanguage { get; set; } = string.Empty;

    /// <summary>The manifest's file row; its path is the Domain <c>ProjectFilePath</c>.</summary>
    public Guid? ProjectFileId { get; set; }

    public string RootDirectory { get; set; } = string.Empty;

    public string PropertiesJson { get; set; } = "{}";

    public string SolutionMembershipsJson { get; set; } = "[]";

    public Guid LastSeenRunId { get; set; }
}
