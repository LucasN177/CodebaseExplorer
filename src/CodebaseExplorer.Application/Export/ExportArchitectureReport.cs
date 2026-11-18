using System.Text;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Export;
using CodebaseExplorer.Contracts.Graph;

namespace CodebaseExplorer.Application.Export;

/// <summary>
/// Use case: generate the Markdown/Mermaid architecture report of an analyzed repository (EXPORT-001, USER_WORKFLOWS.md
/// WF-08) from its stored model. Only generates text; writing the file (and confirming a target inside the repository,
/// SECURITY.md S5) is the caller's job (EXPORT-002).
/// </summary>
public sealed class ExportArchitectureReport(IArchitectureModelStore store, IGraphProjector projector, TimeProvider timeProvider)
{
    /// <summary>Node budget of the diagram for the whole repository: larger Mermaid diagrams are unreadable.</summary>
    public const int WholeRepositoryNodeBudget = 100;

    public async Task<Result<ArchitectureReportDto>> ExecuteAsync(ArchitectureReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RepositoryContractMapping.TryParseRepositoryId(request.RepositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (request.Sections.Count == 0)
        {
            return ApplicationError.Validation(ExportErrorCodes.NoSections, "Choose at least one section.");
        }

        if (request.Sections.FirstOrDefault(s => !ReportSections.All.Contains(s, StringComparer.Ordinal)) is { } unknown)
        {
            return ApplicationError.Validation(ExportErrorCodes.SectionUnknown, $"The report section '{unknown}' does not exist.");
        }

        if (await store.LoadCurrentModelAsync(id, cancellationToken) is not { } stored)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotAnalyzed, "The repository has not been analyzed yet.");
        }

        var query = request.View is { } view
            ? view with { RepositoryId = request.RepositoryId, IncludeActivity = false }
            : new GraphQuery
            {
                RepositoryId = request.RepositoryId,
                Level = GraphLevels.Project,
                NodeBudget = WholeRepositoryNodeBudget,
                Filters = GraphFilters.Default with { IncludeTests = true },
            };
        var projected = projector.Project(stored.Model, query);
        if (!projected.TryGetValue(out var graph, out var graphError))
        {
            return graphError;
        }

        var generatedAt = timeProvider.GetUtcNow();
        var sections = ReportSections.All.Where(s => request.Sections.Contains(s, StringComparer.Ordinal)).ToList();
        var (markdown, truncated) = MarkdownReportRenderer.Render(new ArchitectureReportData(
            stored.Repository, stored.Run, stored.Model, graph, request.View is not null, sections, generatedAt));

        return new ArchitectureReportDto(
            $"ARCHITECTURE-{FileNamePart(stored.Repository.Name)}-{generatedAt.UtcDateTime:yyyy-MM-dd}.md",
            markdown,
            stored.Repository.Name,
            stored.Repository.Location.LocalPath,
            truncated);
    }

    /// <summary>Letters, digits, <c>.</c>, <c>_</c> and <c>-</c>; everything else becomes <c>-</c> (safe on every OS).</summary>
    public static string FileNamePart(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-');
        }

        var part = builder.ToString().Trim('.', '-');
        return part.Length == 0 ? "repository" : part[..Math.Min(part.Length, 80)];
    }
}

/// <summary>Stable <see cref="ApplicationError"/> codes of the export (EXPORT-001/002).</summary>
public static class ExportErrorCodes
{
    public const string NoSections = "export.no-sections";
    public const string SectionUnknown = "export.section-unknown";
}
