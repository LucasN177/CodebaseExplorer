using System.ComponentModel;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// Architecture Explorer (UI-003, USER_WORKFLOWS.md WF-03): shows the project-level graph of the
/// current repository and, in a simple side panel, name and kind of the selection and, for a
/// selected edge, the evidence of its relationships (GRAPH-010). The full Component Inspector
/// follows in UI-006/UI-007.
/// </summary>
public sealed partial class ArchitectureExplorerPageViewModel : PageViewModel
{
    /// <summary>At most this many evidence records are listed for one edge.</summary>
    public const int MaxEvidenceItems = 50;

    private readonly GetGraphView _getGraphView;
    private readonly GetGraphEdgeDetails _getEdgeDetails;
    private readonly RepositorySession _session;
    private readonly ShellStatus _shell;
    private readonly ILogger<ArchitectureExplorerPageViewModel> _logger;
    private int _loadNumber;
    private int _evidenceNumber;
    private GraphQuery? _query;

    public ArchitectureExplorerPageViewModel(
        IGraphViewHost graph,
        GetGraphView getGraphView,
        GetGraphEdgeDetails getEdgeDetails,
        RepositorySession session,
        ShellStatus shell,
        ILogger<ArchitectureExplorerPageViewModel> logger)
    {
        Graph = graph;
        _getGraphView = getGraphView;
        _getEdgeDetails = getEdgeDetails;
        _session = session;
        _shell = shell;
        _logger = logger;

        _session.CurrentChanged += (_, _) => _ = LoadCurrentRepositoryAsync();
        Graph.PropertyChanged += OnGraphPropertyChanged;
        SelectionEvidence.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEvidence));
    }

    public override string Title => "Architecture Explorer";

    public override string Glyph => "◇";

    /// <summary>The graph web view; bound to the graph host control.</summary>
    public IGraphViewHost Graph { get; }

    /// <summary>Name of the repository whose graph is shown.</summary>
    [ObservableProperty]
    public partial string? RepositoryName { get; private set; }

    /// <summary>True once a graph was produced for the current repository (shows the graph instead of the empty state).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool HasGraph { get; private set; }

    public bool ShowEmptyState => !HasGraph;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Why the graph could not be shown; displayed inline.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial string? SelectionName { get; private set; }

    /// <summary>Readable kind of the selected element, e.g. <c>Project</c>.</summary>
    [ObservableProperty]
    public partial string? SelectionKind { get; private set; }

    /// <summary>Secondary information, e.g. the project's folder or the number of relationships.</summary>
    [ObservableProperty]
    public partial string? SelectionDetail { get; private set; }

    /// <summary>Set when more than one element is selected, e.g. <c>3 elements selected</c>.</summary>
    [ObservableProperty]
    public partial string? SelectionCountText { get; private set; }

    public bool HasSelection => SelectionName is not null;

    /// <summary>Evidence of the selected edge's relationships (empty for nodes).</summary>
    public System.Collections.ObjectModel.ObservableCollection<EvidenceItem> SelectionEvidence { get; } = [];

    public bool HasEvidence => SelectionEvidence.Count > 0;

    /// <summary>State of the evidence list, e.g. <c>Loading evidence…</c>, an error, or how many records were omitted.</summary>
    [ObservableProperty]
    public partial string? EvidenceMessage { get; private set; }

    /// <summary>Loads the project-level graph of <see cref="RepositorySession.Current"/> into the graph view.</summary>
    public async Task LoadCurrentRepositoryAsync(CancellationToken cancellationToken = default)
    {
        if (_session.Current is not { } repository)
        {
            // The repository was closed (removed): back to the empty state; late results of a running load are dropped.
            ++_loadNumber;
            _query = null;
            RepositoryName = null;
            ErrorMessage = null;
            HasGraph = false;
            IsLoading = false;
            UpdateSelection();
            return;
        }

        var loadNumber = ++_loadNumber;
        RepositoryName = repository.Name;
        ErrorMessage = null;
        IsLoading = true;
        using var operation = _shell.BeginOperation($"Loading the graph of {repository.Name}");

        try
        {
            var query = new GraphQuery { RepositoryId = repository.RepositoryId };
            var result = await _getGraphView.ExecuteAsync(query, cancellationToken);
            if (loadNumber != _loadNumber)
            {
                return; // a newer import replaced this one
            }

            _query = query;

            if (!result.TryGetValue(out var view, out var error))
            {
                _logger.LogWarning("Graph for the current repository failed with {ErrorCode}", error.Code);
                HasGraph = false;
                ErrorMessage = error.Message;
                _shell.Notify(NotificationSeverity.Error, $"The graph of {repository.Name} could not be shown: {error.Message}");
                return;
            }

            HasGraph = true;
            if (!await Graph.ShowGraphAsync(view, cancellationToken))
            {
                ErrorMessage = "The graph view did not accept the graph. Details are in the application log.";
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Loading the graph failed unexpectedly");
            ErrorMessage = "The graph could not be loaded. Details are in the application log.";
            _shell.Notify(NotificationSeverity.Error, ErrorMessage);
        }
        finally
        {
            if (loadNumber == _loadNumber)
            {
                IsLoading = false;
            }
        }
    }

    private void OnGraphPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IGraphViewHost.Selection) or nameof(IGraphViewHost.CurrentGraph))
        {
            UpdateSelection();
        }
    }

    private void UpdateSelection()
    {
        var selection = Graph.Selection;
        var graph = Graph.CurrentGraph;
        var count = selection.NodeIds.Count + selection.EdgeIds.Count;
        SelectionCountText = count > 1 ? $"{count} elements selected" : null;

        if (graph is not null && selection.NodeIds.Count > 0 && graph.Nodes.FirstOrDefault(n => n.Id == selection.NodeIds[0]) is { } node)
        {
            SelectionKind = KindText(node);
            SelectionDetail = node.Sublabel;
            SelectionName = node.Label;
            ClearEvidence();
        }
        else if (graph is not null && selection.EdgeIds.Count > 0 && graph.Edges.FirstOrDefault(e => e.Id == selection.EdgeIds[0]) is { } edge)
        {
            var labels = graph.Nodes.ToDictionary(n => n.Id, n => n.Label, StringComparer.Ordinal);
            SelectionKind = "Dependency";
            SelectionDetail = edge.Count == 1 ? "1 relationship" : $"{edge.Count} relationships";
            SelectionName = $"{labels.GetValueOrDefault(edge.Source, "?")} → {labels.GetValueOrDefault(edge.Target, "?")}";
            _ = LoadEvidenceAsync(edge.Id);
        }
        else
        {
            SelectionKind = null;
            SelectionDetail = null;
            SelectionName = null;
            ClearEvidence();
        }
    }

    private async Task LoadEvidenceAsync(string edgeId)
    {
        var evidenceNumber = ++_evidenceNumber;
        SelectionEvidence.Clear();
        if (_query is not { } query)
        {
            EvidenceMessage = null;
            return;
        }

        EvidenceMessage = "Loading evidence…";
        try
        {
            var result = await _getEdgeDetails.ExecuteAsync(query, edgeId, CancellationToken.None);
            if (evidenceNumber != _evidenceNumber)
            {
                return; // the selection changed meanwhile
            }

            if (!result.TryGetValue(out var details, out var error))
            {
                _logger.LogWarning("Evidence for the selected edge failed with {ErrorCode}", error.Code);
                EvidenceMessage = $"Evidence could not be loaded: {error.Message}";
                return;
            }

            var items = details.Relationships
                .SelectMany(relationship => relationship.Evidence.Select(evidence => ToItem(details, relationship, evidence)))
                .ToList();
            foreach (var item in items.Take(MaxEvidenceItems))
            {
                SelectionEvidence.Add(item);
            }

            EvidenceMessage = items.Count > MaxEvidenceItems ? $"{items.Count - MaxEvidenceItems} more evidence records not listed" : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Loading evidence failed unexpectedly");
            if (evidenceNumber == _evidenceNumber)
            {
                EvidenceMessage = "Evidence could not be loaded. Details are in the application log.";
            }
        }
    }

    private void ClearEvidence()
    {
        ++_evidenceNumber;
        SelectionEvidence.Clear();
        EvidenceMessage = null;
    }

    private static EvidenceItem ToItem(GraphEdgeDetailsDto edge, GraphRelationshipDto relationship, GraphEvidenceDto evidence)
    {
        // At the project level the endpoints usually are the edge's nodes; name them only when they differ (files, types …).
        var endpoints = relationship.SourceLabel == edge.SourceLabel && relationship.TargetLabel == edge.TargetLabel
            ? string.Empty
            : $" · {relationship.SourceLabel} → {relationship.TargetLabel}";
        var location = evidence.Path is null ? "no file location"
            : evidence.StartLine is not { } start ? evidence.Path
            : evidence.EndLine is { } end && end > start ? $"{evidence.Path}:{start}–{end}"
            : $"{evidence.Path}:{start}";

        return new EvidenceItem($"{relationship.Kind} · {evidence.Level}{endpoints}", location, $"{evidence.Analyzer} · {evidence.Rule}");
    }

    private static string KindText(GraphNodeDto node)
    {
        var kind = node.Kind switch
        {
            GraphNodeKinds.Project => "Project",
            GraphNodeKinds.ExternalDependency => "External dependency",
            _ => node.Kind,
        };

        return node.Badges.Contains(GraphBadges.Missing) ? $"{kind} (project file missing)" : kind;
    }
}
