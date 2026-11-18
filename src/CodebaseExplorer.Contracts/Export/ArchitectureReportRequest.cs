using CodebaseExplorer.Contracts.Graph;

namespace CodebaseExplorer.Contracts.Export;

/// <summary>What to put into a Markdown architecture report (EXPORT-001, USER_WORKFLOWS.md WF-08).</summary>
public sealed record ArchitectureReportRequest
{
    /// <summary>Repository id (32 hex characters).</summary>
    public required string RepositoryId { get; init; }

    /// <summary>Sections in report order; tokens of <see cref="ReportSections"/>. Default: all.</summary>
    public IReadOnlyList<string> Sections { get; init; } = ReportSections.All;

    /// <summary>
    /// The graph view to draw (scope "current view": level, expansions, filters, neighborhood). <see langword="null"/> for
    /// the whole repository: the project level with test projects, without external packages.
    /// </summary>
    public GraphQuery? View { get; init; }
}

/// <summary>Section tokens of <see cref="ArchitectureReportRequest.Sections"/>.</summary>
public static class ReportSections
{
    public const string Overview = "overview";
    public const string Languages = "languages";
    public const string Projects = "projects";
    public const string DependencyGraph = "dependency-graph";
    public const string ExternalDependencies = "external-dependencies";
    public const string AnalysisMetadata = "analysis-metadata";
    public const string EvidenceStatistics = "evidence-statistics";

    /// <summary>All sections in report order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Overview, Languages, Projects, DependencyGraph, ExternalDependencies, AnalysisMetadata, EvidenceStatistics];
}

/// <summary>A generated report: Markdown text and a suggested file name. Writing it is the caller's job.</summary>
/// <param name="FileName">Suggested file name, <c>ARCHITECTURE-&lt;repository&gt;-&lt;yyyy-MM-dd&gt;.md</c> (file-name safe).</param>
/// <param name="Markdown">The report (LF line endings, UTF-8 when written).</param>
/// <param name="RepositoryName">Name of the repository.</param>
/// <param name="RepositoryRootPath">Absolute path of the analyzed folder (to warn before writing into it).</param>
/// <param name="GraphTruncated">The diagram shows only part of the graph (node budget or Mermaid edge limit).</param>
public sealed record ArchitectureReportDto(
    string FileName,
    string Markdown,
    string RepositoryName,
    string RepositoryRootPath,
    bool GraphTruncated);
