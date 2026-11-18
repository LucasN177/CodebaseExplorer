using System.ComponentModel;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// Architecture Explorer (UI-003, USER_WORKFLOWS.md WF-03): shows the project-level graph of the
/// current repository and, in a simple side panel, name and kind of the selection (the full
/// Component Inspector follows in UI-006).
/// </summary>
public sealed partial class ArchitectureExplorerPageViewModel : PageViewModel
{
    private readonly GetGraphView _getGraphView;
    private readonly RepositorySession _session;
    private readonly ShellStatus _shell;
    private readonly ILogger<ArchitectureExplorerPageViewModel> _logger;
    private int _loadNumber;

    public ArchitectureExplorerPageViewModel(
        IGraphViewHost graph,
        GetGraphView getGraphView,
        RepositorySession session,
        ShellStatus shell,
        ILogger<ArchitectureExplorerPageViewModel> logger)
    {
        Graph = graph;
        _getGraphView = getGraphView;
        _session = session;
        _shell = shell;
        _logger = logger;

        _session.CurrentChanged += (_, _) => _ = LoadCurrentRepositoryAsync();
        Graph.PropertyChanged += OnGraphPropertyChanged;
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

    /// <summary>Loads the project-level graph of <see cref="RepositorySession.Current"/> into the graph view.</summary>
    public async Task LoadCurrentRepositoryAsync(CancellationToken cancellationToken = default)
    {
        if (_session.Current is not { } repository)
        {
            return;
        }

        var loadNumber = ++_loadNumber;
        RepositoryName = repository.Name;
        ErrorMessage = null;
        IsLoading = true;
        using var operation = _shell.BeginOperation($"Loading the graph of {repository.Name}");

        try
        {
            var result = await _getGraphView.ExecuteAsync(new GraphQuery { RepositoryId = repository.RepositoryId }, cancellationToken);
            if (loadNumber != _loadNumber)
            {
                return; // a newer import replaced this one
            }

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
        }
        else if (graph is not null && selection.EdgeIds.Count > 0 && graph.Edges.FirstOrDefault(e => e.Id == selection.EdgeIds[0]) is { } edge)
        {
            var labels = graph.Nodes.ToDictionary(n => n.Id, n => n.Label, StringComparer.Ordinal);
            SelectionKind = "Dependency";
            SelectionDetail = edge.Count == 1 ? "1 relationship" : $"{edge.Count} relationships";
            SelectionName = $"{labels.GetValueOrDefault(edge.Source, "?")} → {labels.GetValueOrDefault(edge.Target, "?")}";
        }
        else
        {
            SelectionKind = null;
            SelectionDetail = null;
            SelectionName = null;
        }
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
