using System.Globalization;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// Repository overview (UI-004, USER_WORKFLOWS.md WF-01 step 5): languages, projects by ecosystem, external
/// dependencies, the run of the current model and its warnings/errors, with a link to the explorer. Shown
/// after an import or when a stored repository is opened.
/// </summary>
public sealed partial class OverviewPageViewModel : PageViewModel
{
    private readonly GetRepositoryOverview _getOverview;
    private readonly RepositorySession _session;
    private readonly ILogger<OverviewPageViewModel> _logger;
    private int _loadNumber;

    public OverviewPageViewModel(GetRepositoryOverview getOverview, RepositorySession session, ILogger<OverviewPageViewModel> logger)
    {
        _getOverview = getOverview;
        _session = session;
        _logger = logger;

        _session.CurrentChanged += (_, _) => _ = LoadAsync();
    }

    public override string Title => "Overview";

    public override string IconKey => "overview";

    /// <summary>Raised when the user asks to open the Architecture Explorer; the shell navigates.</summary>
    public event EventHandler? ExplorerRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverview), nameof(ShowEmptyState), nameof(Name), nameof(RootPath), nameof(Tiles),
        nameof(Languages), nameof(LanguageRows), nameof(Ecosystems), nameof(HasNoProjects), nameof(MissingProjectsText), nameof(ModelRunText), nameof(NewerRunText))]
    public partial RepositoryOverviewDto? Overview { get; private set; }

    public bool HasOverview => Overview is not null;

    public bool ShowEmptyState => Overview is null && ErrorMessage is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial string? ErrorMessage { get; private set; }

    public string? Name => Overview?.Name;

    public string? RootPath => Overview?.RootPath;

    /// <summary>Key figures, e.g. <c>Files 19</c>.</summary>
    public IReadOnlyList<OverviewTile> Tiles => Overview is not { } overview ? [] :
    [
        new("Files", Number(overview.FileCount)),
        new("Projects", Number(overview.ProjectsByEcosystem.Sum(e => e.ProjectCount))),
        new("Packages", Number(overview.ExternalDependencyCount)),
        new("Relationships", Number(overview.RelationshipCount)),
        new("Warnings", Number(overview.WarningCount)),
        new("Errors", Number(overview.ErrorCount)),
    ];

    /// <summary>E.g. <c>C# · 9 files · 101 lines</c>.</summary>
    public IReadOnlyList<string> Languages => Overview?.Languages
        .Select(l => $"{LanguageName(l.Language)} · {RunItemViewModel.Count(l.FileCount, "file")} · {RunItemViewModel.Count((int)Math.Min(l.LineCount, int.MaxValue), "line")}")
        .ToList() ?? [];

    /// <summary>The languages as table rows with their share of the files (for the bar), largest first.</summary>
    public IReadOnlyList<LanguageRow> LanguageRows
    {
        get
        {
            if (Overview?.Languages is not { Count: > 0 } languages)
            {
                return [];
            }

            var max = Math.Max(1, languages.Max(l => l.FileCount));
            return [.. languages.Select(l => new LanguageRow(
                LanguageName(l.Language),
                RunItemViewModel.Count(l.FileCount, "file"),
                RunItemViewModel.Count((int)Math.Min(l.LineCount, int.MaxValue), "line"),
                100.0 * l.FileCount / max))];
        }
    }

    /// <summary>E.g. <c>.NET · 5 projects</c>.</summary>
    public IReadOnlyList<string> Ecosystems => Overview?.ProjectsByEcosystem
        .Select(e => $"{EcosystemName(e.Ecosystem)} · {RunItemViewModel.Count(e.ProjectCount, "project")}")
        .ToList() ?? [];

    /// <summary>True when the analysis found no projects (WF-01 edge case): only file statistics exist.</summary>
    public bool HasNoProjects => Overview is { ProjectsByEcosystem.Count: 0 };

    public string? MissingProjectsText => Overview is { MissingProjectCount: > 0 } overview
        ? overview.MissingProjectCount == 1
            ? "1 referenced project file does not exist (see the warnings in the run history)."
            : $"{overview.MissingProjectCount} referenced project files do not exist (see the warnings in the run history)."
        : null;

    /// <summary>E.g. <c>Succeeded · 18.11.2026 10:30 · 1.2 s</c>.</summary>
    public string? ModelRunText => Overview?.ModelRun is { } run ? RunText(run) : null;

    /// <summary>Set when a newer run (failed or cancelled) did not replace the shown result.</summary>
    public string? NewerRunText => Overview?.NewerRun is { } run
        ? $"A newer analysis did not replace this result: {RunText(run)}."
        : null;

    public override Task ActivateAsync() => LoadAsync();

    /// <summary>Loads the overview of the open repository.</summary>
    public async Task LoadAsync()
    {
        var loadNumber = ++_loadNumber;
        ErrorMessage = null;
        if (_session.Current is not { } repository)
        {
            Overview = null;
            return;
        }

        try
        {
            var result = await _getOverview.ExecuteAsync(repository.RepositoryId, CancellationToken.None);
            if (loadNumber != _loadNumber)
            {
                return; // the repository changed meanwhile
            }

            if (!result.TryGetValue(out var overview, out var error))
            {
                _logger.LogWarning("Overview failed with {ErrorCode}", error.Code);
                Overview = null;
                ErrorMessage = error.Message;
                return;
            }

            Overview = overview;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Loading the overview failed unexpectedly");
            Overview = null;
            ErrorMessage = "The overview could not be loaded. Details are in the application log.";
        }
    }

    [RelayCommand]
    private void OpenExplorer() => ExplorerRequested?.Invoke(this, EventArgs.Empty);

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string RunText(RunInfoDto run)
    {
        var text = $"{RunStatusText.Of(run.Status)} · {run.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";
        return run.Duration is { } duration
            ? $"{text} · {duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)} s"
            : text;
    }

    private static string LanguageName(string language) => language switch
    {
        "csharp" => "C#",
        "typescript" => "TypeScript",
        "tsx" => "TypeScript (TSX)",
        "javascript" => "JavaScript",
        "python" => "Python",
        "json" => "JSON",
        "xml" => "XML",
        "markdown" => "Markdown",
        "sln" => "Solution files",
        "unknown" => "Other",
        _ => language,
    };

    private static string EcosystemName(string ecosystem) => ecosystem switch
    {
        "dotnet" => ".NET",
        "node" => "Node.js",
        "python" => "Python",
        _ => "Other",
    };
}

/// <summary>A key figure of the overview.</summary>
/// <param name="Label">What is counted.</param>
/// <param name="Value">The formatted count.</param>
public sealed record OverviewTile(string Label, string Value);

/// <summary>A language of the overview table; <c>Share</c> is 0–100 relative to the language with the most files.</summary>
public sealed record LanguageRow(string Name, string Files, string Lines, double Share);
