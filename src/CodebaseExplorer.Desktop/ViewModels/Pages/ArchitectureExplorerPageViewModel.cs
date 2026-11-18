using System.ComponentModel;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Inspector;
using CodebaseExplorer.Application.Search;
using CodebaseExplorer.Application.Source;
using CodebaseExplorer.Contracts.Inspector;
using CodebaseExplorer.Application.Settings;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly GetSourceSnippet _getSnippet;
    private readonly GetElementDetails _getDetails;
    private readonly SearchElements _search;
    private int _searchNumber;
    private bool _suppressFilterChanges;

    /// <summary>Earlier view states for <see cref="BackCommand"/> (GRAPH-009), newest last.</summary>
    private readonly List<GraphQuery> _history = [];

    /// <summary>At most this many view states are remembered for <c>Back</c>.</summary>
    public const int MaxHistory = 50;
    private int _detailsNumber;
    private readonly IClipboardService _clipboard;
    private int _snippetNumber;
    private string? _snippetAbsolutePath;
    private readonly RepositorySession _session;
    private readonly IUserSettingsStore _settings;
    private readonly ShellStatus _shell;
    private readonly ILogger<ArchitectureExplorerPageViewModel> _logger;
    private int _loadNumber;
    private int _evidenceNumber;
    private GraphQuery? _query;
    private bool _restoringLayout;

    public ArchitectureExplorerPageViewModel(
        IGraphViewHost graph,
        GetGraphView getGraphView,
        GetGraphEdgeDetails getEdgeDetails,
        GetSourceSnippet getSnippet,
        GetElementDetails getDetails,
        SearchElements search,
        IClipboardService clipboard,
        RepositorySession session,
        IUserSettingsStore settings,
        ShellStatus shell,
        ILogger<ArchitectureExplorerPageViewModel> logger)
    {
        Graph = graph;
        _getGraphView = getGraphView;
        _getEdgeDetails = getEdgeDetails;
        _getSnippet = getSnippet;
        _getDetails = getDetails;
        _search = search;
        RelationshipKindFilters = [.. RelationshipKindNames.Select(k => new FilterOption(k.Token, k.Display, OnFilterOptionChanged))];
        MinLevelOptions = [new(null, "All evidence"), new(GraphEvidenceLevels.Syntactic, "Syntactic or confirmed"), new(GraphEvidenceLevels.Confirmed, "Confirmed only")];
        DirectionOptions = [new(GraphDirections.Both, "Both directions"), new(GraphDirections.Outgoing, "Outgoing"), new(GraphDirections.Incoming, "Incoming")];
        ViewModes = [new(GraphLevels.Project, "Projects"), new(GraphLevels.Repository, "Grouped")];
        _suppressFilterChanges = true;
        SelectedViewMode = ViewModes[0];
        SelectedMinLevel = MinLevelOptions[0];
        SelectedDirection = DirectionOptions[0];
        _suppressFilterChanges = false;
        _clipboard = clipboard;
        _session = session;
        _settings = settings;
        _shell = shell;
        _logger = logger;
        Layouts = [.. Enum.GetValues<GraphLayout>().Select(layout => new GraphLayoutOption(layout, layout.DisplayName()))];
        _restoringLayout = true; // the initial value is neither sent nor stored; RestoreLayoutAsync applies the stored one
        SelectedLayout = OptionFor(GraphLayouts.Default);
        _restoringLayout = false;

        _session.CurrentChanged += (_, _) => _ = LoadCurrentRepositoryAsync();
        Graph.PropertyChanged += OnGraphPropertyChanged;
        Graph.ExpandRequested += (_, e) => _ = SetExpandedAsync(e.ElementId, expanded: true);
        Graph.CollapseRequested += (_, e) => _ = SetExpandedAsync(e.ElementId, expanded: false);
        SelectionEvidence.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEvidence));
        SnippetLines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSnippet));
        InspectorMembers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMembers));
        IncomingGroups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasIncoming));
        OutgoingGroups.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOutgoing));
    }

    public override string Title => "Architecture Explorer";

    public override string Glyph => "◇";

    /// <summary>The graph web view; bound to the graph host control.</summary>
    public IGraphViewHost Graph { get; }

    /// <summary>The layouts offered by the layout switcher (GRAPH-005).</summary>
    public IReadOnlyList<GraphLayoutOption> Layouts { get; }

    /// <summary>The chosen layout; a user change re-arranges the graph and stores the choice (<see cref="UserSettingKeys.GraphLayout"/>).</summary>
    [ObservableProperty]
    public partial GraphLayoutOption SelectedLayout { get; set; }

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

    /// <summary>What the node budget left out of the shown graph and how to see it (GRAPH-008); null if nothing.</summary>
    [ObservableProperty]
    public partial string? TruncationText { get; private set; }

    private void ShowTruncation(GraphViewDto view) =>
        TruncationText = view.Truncation is { } truncation
            ? (truncation.Reason == TruncationReasons.ExpansionRefused
                ? $"Not expanded: {truncation.OmittedNodeCount} more elements would exceed the limit of {_query?.NodeBudget ?? GraphQuery.DefaultNodeBudget} nodes. "
                : $"{truncation.OmittedNodeCount} elements are not shown individually (limit {_query?.NodeBudget ?? GraphQuery.DefaultNodeBudget} nodes). ")
              + string.Join(" ", truncation.Suggestions.Skip(1))
            : null;

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

    /// <summary>
    /// Applies the stored layout choice to the graph view without storing it again. Called once at startup;
    /// a missing, unknown or unreadable setting means <see cref="GraphLayouts.Default"/> (read errors are logged).
    /// </summary>
    public async Task RestoreLayoutAsync(CancellationToken cancellationToken = default)
    {
        string? stored;
        try
        {
            stored = await _settings.GetAsync(UserSettingKeys.GraphLayout, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "The stored graph layout could not be read; the default layout is used");
            stored = null;
        }

        _restoringLayout = true;
        try
        {
            SelectedLayout = OptionFor(GraphLayouts.Parse(stored));
        }
        finally
        {
            _restoringLayout = false;
        }

        await Graph.SetLayoutAsync(SelectedLayout.Layout, cancellationToken);
    }

    /// <summary>Loads the project-level graph of <see cref="RepositorySession.Current"/> into the graph view.</summary>
    public async Task LoadCurrentRepositoryAsync(CancellationToken cancellationToken = default)
    {
        if (_session.Current is not { } repository)
        {
            // The repository was closed (removed): back to the empty state; late results of a running load are dropped.
            ++_loadNumber;
            _query = null;
            TruncationText = null;
            ResetNavigation();
            RepositoryName = null;
            ErrorMessage = null;
            HasGraph = false;
            IsLoading = false;
            UpdateSelection();
            return;
        }

        var loadNumber = ++_loadNumber;
        ResetNavigation();
        RepositoryName = repository.Name;
        ErrorMessage = null;
        IsLoading = true;
        using var operation = _shell.BeginOperation($"Loading the graph of {repository.Name}");

        try
        {
            var query = new GraphQuery { RepositoryId = repository.RepositoryId, Filters = CurrentFilters() };
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
            ShowTruncation(view);
            ShowNavigation(view, query);
            OfferFilterValues(view);
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

    /// <summary>
    /// Expands or collapses a node (GRAPH-006, double click in the graph): recomputes the view with the changed set of
    /// expanded nodes and sends only the difference. Nodes expanded inside a collapsed node are remembered, so
    /// expanding it again restores them; the projection ignores them while they are hidden.
    /// </summary>
    public async Task SetExpandedAsync(string elementId, bool expanded, CancellationToken cancellationToken = default)
    {
        if (_query is not { } query)
        {
            return;
        }

        var set = query.Expanded.ToHashSet(StringComparer.Ordinal);
        if (!(expanded ? set.Add(elementId) : set.Remove(elementId)))
        {
            return;
        }

        var loadNumber = _loadNumber;
        var next = query with { Expanded = [.. set.Order(StringComparer.Ordinal)] };
        try
        {
            var result = await _getGraphView.ExecuteAsync(next, cancellationToken);
            if (loadNumber != _loadNumber)
            {
                return; // another repository was loaded meanwhile
            }

            if (!result.TryGetValue(out var view, out var error))
            {
                _logger.LogWarning("Expanding or collapsing failed with {ErrorCode}", error.Code);
                ErrorMessage = error.Message;
                return;
            }

            Remember(query);
            _query = next;
            ShowTruncation(view);
            if (!await Graph.PatchGraphAsync(view, cancellationToken))
            {
                ErrorMessage = "The graph view did not accept the change. Details are in the application log.";
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Expanding or collapsing failed unexpectedly");
            ErrorMessage = "The graph could not be changed. Details are in the application log.";
        }
    }

    partial void OnSelectedLayoutChanged(GraphLayoutOption value)
    {
        if (!_restoringLayout)
        {
            _ = ChangeLayoutAsync(value.Layout);
        }
    }

    private async Task ChangeLayoutAsync(GraphLayout layout)
    {
        try
        {
            await Graph.SetLayoutAsync(layout);
            await _settings.SetAsync(UserSettingKeys.GraphLayout, layout.ToToken(), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The bridge logs delivery problems itself; this is the storage failing.
            _logger.LogError(exception, "Saving the graph layout failed");
            _shell.Notify(NotificationSeverity.Error, "The graph layout could not be saved and will be reset at the next start.");
        }
    }

    private GraphLayoutOption OptionFor(GraphLayout layout) => Layouts.First(option => option.Layout == layout);

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
            SelectionDetail = node.Kind == GraphNodeKinds.Entity ? null : node.Sublabel; // an entity's sublabel is its kind, shown above
            SelectionName = node.Label;
            ClearEvidence();
            if (node.Kind is GraphNodeKinds.Aggregate or GraphNodeKinds.Group or GraphNodeKinds.Folder)
            {
                ClearDetails(); // not a model element: there is nothing to inspect
                SelectionDetail = node.Sublabel;
            }
            else
            {
                _ = LoadDetailsAsync(node.Id);
            }
        }
        else if (graph is not null && selection.EdgeIds.Count > 0 && graph.Edges.FirstOrDefault(e => e.Id == selection.EdgeIds[0]) is { } edge)
        {
            var labels = graph.Nodes.ToDictionary(n => n.Id, n => n.Label, StringComparer.Ordinal);
            SelectionKind = "Dependency";
            SelectionDetail = edge.Count == 1 ? "1 relationship" : $"{edge.Count} relationships";
            SelectionName = $"{labels.GetValueOrDefault(edge.Source, "?")} → {labels.GetValueOrDefault(edge.Target, "?")}";
            ClearDetails();
            _ = LoadEvidenceAsync(edge.Id);
        }
        else
        {
            SelectionKind = null;
            SelectionDetail = null;
            SelectionName = null;
            ClearEvidence();
            ClearDetails();
        }
    }

    private async Task LoadEvidenceAsync(string edgeId)
    {
        var evidenceNumber = ++_evidenceNumber;
        SelectedEvidence = null;
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
        SelectedEvidence = null;
        SelectionEvidence.Clear();
        EvidenceMessage = null;
    }

    // ---- Component Inspector (UI-006) ----

    /// <summary>Facts of the inspected node (path, ecosystem, visibility, declarations, …).</summary>
    public System.Collections.ObjectModel.ObservableCollection<InspectorFact> InspectorFacts { get; } = [];

    /// <summary>Members of a type or types of a namespace.</summary>
    public System.Collections.ObjectModel.ObservableCollection<InspectorItem> InspectorMembers { get; } = [];

    public System.Collections.ObjectModel.ObservableCollection<InspectorGroup> IncomingGroups { get; } = [];

    public System.Collections.ObjectModel.ObservableCollection<InspectorGroup> OutgoingGroups { get; } = [];

    /// <summary>E.g. <c>fan-in 3 · fan-out 5 · size 12</c>; null while no node is inspected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    public partial string? InspectorMetrics { get; private set; }

    public bool HasDetails => InspectorMetrics is not null;

    public bool HasMembers => InspectorMembers.Count > 0;

    public bool HasIncoming => IncomingGroups.Count > 0;

    public bool HasOutgoing => OutgoingGroups.Count > 0;

    /// <summary>Loading state, an error, or why "show in graph" was not possible.</summary>
    [ObservableProperty]
    public partial string? InspectorMessage { get; private set; }

    private void ClearDetails()
    {
        ++_detailsNumber;
        InspectorFacts.Clear();
        InspectorMembers.Clear();
        IncomingGroups.Clear();
        OutgoingGroups.Clear();
        InspectorMetrics = null;
        InspectorMessage = null;
    }

    /// <summary>Loads the inspector details of a selected node; late results of a previous selection are dropped.</summary>
    private async Task LoadDetailsAsync(string elementId)
    {
        ClearDetails();
        var detailsNumber = _detailsNumber;
        if (_query is not { } query)
        {
            return;
        }

        InspectorMessage = "Loading details…";
        try
        {
            var result = await _getDetails.ExecuteAsync(query.RepositoryId, elementId, CancellationToken.None);
            if (detailsNumber != _detailsNumber)
            {
                return;
            }

            if (!result.TryGetValue(out var details, out var error))
            {
                _logger.LogWarning("Inspector details failed with {ErrorCode}", error.Code);
                InspectorMessage = error.Message;
                return;
            }

            InspectorMessage = null;
            foreach (var fact in details.Facts)
            {
                InspectorFacts.Add(new InspectorFact(fact.Label, fact.Value));
            }

            foreach (var member in details.Members)
            {
                InspectorMembers.Add(InspectorItem.Member(member));
            }

            foreach (var group in details.Incoming)
            {
                IncomingGroups.Add(new InspectorGroup($"{group.Kind} ({group.Items.Count})", [.. group.Items.Select(InspectorItem.From)]));
            }

            foreach (var group in details.Outgoing)
            {
                OutgoingGroups.Add(new InspectorGroup($"{group.Kind} ({group.Items.Count})", [.. group.Items.Select(InspectorItem.From)]));
            }

            InspectorMetrics = $"fan-in {details.FanIn} · fan-out {details.FanOut} · size {details.Size}";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Loading inspector details failed unexpectedly");
            if (detailsNumber == _detailsNumber)
            {
                InspectorMessage = "The details could not be loaded. Details are in the application log.";
            }
        }
    }

    /// <summary>
    /// "Show in graph" (UI-006): expands the project and namespaces that contain the element, then selects and centers
    /// its node (a member is shown by its type, a file by its project).
    /// </summary>
    [RelayCommand]
    private async Task ShowInGraphAsync(InspectorItem? item)
    {
        if (item?.Reference.GraphNodeId is not { } nodeId || _query is not { } query)
        {
            return;
        }

        var expanded = query.Expanded.ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var ancestor in item.Reference.ExpandPath)
        {
            changed |= expanded.Add(ancestor); // all of them, not only the first new one
        }

        if (changed)
        {
            var next = query with { Expanded = [.. expanded.Order(StringComparer.Ordinal)] };
            var result = await _getGraphView.ExecuteAsync(next, CancellationToken.None);
            if (!result.TryGetValue(out var view, out var error))
            {
                InspectorMessage = error.Message;
                return;
            }

            Remember(query);
            _query = next;
            ShowTruncation(view);
            await Graph.PatchGraphAsync(view, CancellationToken.None);
        }

        if (!await Graph.FocusAsync(nodeId, CancellationToken.None))
        {
            InspectorMessage = item.Reference.Kind == "external-dependency"
                ? $"{item.Label} is not shown: external dependencies are hidden in the graph."
                : $"{item.Label} could not be shown in the graph.";
        }
    }

    /// <summary>The evidence record whose source snippet is shown (UI-007).</summary>
    [ObservableProperty]
    public partial EvidenceItem? SelectedEvidence { get; set; }

    /// <summary>Lines of the source snippet around <see cref="SelectedEvidence"/> (plain text).</summary>
    public System.Collections.ObjectModel.ObservableCollection<SnippetLine> SnippetLines { get; } = [];

    public bool HasSnippet => SnippetLines.Count > 0;

    /// <summary>Repository-relative path of the snippet's file.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    public partial string? SnippetPath { get; private set; }

    /// <summary>A warning (file changed since the analysis) or why no snippet can be shown (file missing, …).</summary>
    [ObservableProperty]
    public partial string? SnippetMessage { get; private set; }

    partial void OnSelectedEvidenceChanged(EvidenceItem? value) => _ = LoadSnippetAsync(value);

    /// <summary>Loads the snippet of an evidence record; late results of a previous selection are dropped.</summary>
    private async Task LoadSnippetAsync(EvidenceItem? evidence)
    {
        var snippetNumber = ++_snippetNumber;
        SnippetLines.Clear();
        SnippetPath = null;
        SnippetMessage = null;
        _snippetAbsolutePath = null;
        if (evidence is not { HasSource: true } || _query is not { } query)
        {
            return;
        }

        try
        {
            var result = await _getSnippet.ExecuteAsync(query.RepositoryId, evidence.Path!, evidence.StartLine!.Value, evidence.EndLine, CancellationToken.None);
            if (snippetNumber != _snippetNumber)
            {
                return;
            }

            if (!result.TryGetValue(out var snippet, out var error))
            {
                _logger.LogWarning("Source snippet failed with {ErrorCode}", error.Code); // never the content
                SnippetPath = evidence.Path;
                SnippetMessage = error.Message;
                return;
            }

            _snippetAbsolutePath = snippet.AbsolutePath; // before SnippetPath: setting it re-evaluates CopyPathCommand
            SnippetPath = snippet.Path;
            foreach (var line in snippet.Lines)
            {
                SnippetLines.Add(new SnippetLine(line.Number, line.Text, line.IsEvidence));
            }

            SnippetMessage = snippet.ChangedSinceAnalysis
                ? "The file changed since the analysis; the lines may have moved. Re-analyze to update the evidence."
                : snippet.RangeTruncated ? "The file is shorter now than the evidence range; only the existing lines are shown." : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Loading a source snippet failed unexpectedly");
            if (snippetNumber == _snippetNumber)
            {
                SnippetMessage = "The source could not be shown. Details are in the application log.";
            }
        }
    }

    /// <summary>Copies the absolute path of the snippet's file to the clipboard.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyPath))]
    private async Task CopyPathAsync()
    {
        if (_snippetAbsolutePath is { } path)
        {
            await _clipboard.SetTextAsync(path);
        }
    }

    private bool CanCopyPath() => SnippetPath is not null && _snippetAbsolutePath is not null;

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

        return new EvidenceItem($"{relationship.Kind} · {evidence.Level}{endpoints}", location, $"{evidence.Analyzer} · {evidence.Rule}",
            evidence.Path, evidence.StartLine, evidence.EndLine);
    }

    // ---- Filters, search and neighborhood (GRAPH-007) ----

    private static readonly (string Token, string Display)[] RelationshipKindNames =
    [
        ("project-reference", "Project references"), ("package-reference", "Package references"), ("framework-reference", "Framework references"),
        ("imports", "Imports"), ("inherits", "Inheritance"), ("implements", "Implementations"), ("uses-type", "Type usages"),
        ("instantiates", "Instantiations"), ("calls", "Calls"), ("registers-service", "DI registrations"), ("depends-on", "Other dependencies"),
    ];

    /// <summary>Languages of the repository's projects; none checked means all.</summary>
    public System.Collections.ObjectModel.ObservableCollection<FilterOption> LanguageFilters { get; } = [];

    /// <summary>Ecosystems of the repository's projects; none checked means all.</summary>
    public System.Collections.ObjectModel.ObservableCollection<FilterOption> EcosystemFilters { get; } = [];

    /// <summary>Relationship kinds; none checked means all.</summary>
    public IReadOnlyList<FilterOption> RelationshipKindFilters { get; }

    public IReadOnlyList<ChoiceOption> MinLevelOptions { get; }

    [ObservableProperty]
    public partial ChoiceOption SelectedMinLevel { get; set; }

    /// <summary>Show test projects and test code (hidden by default, GRAPH_ENGINE.md §3).</summary>
    [ObservableProperty]
    public partial bool IncludeTests { get; set; }

    /// <summary>Show generated code (hidden by default).</summary>
    [ObservableProperty]
    public partial bool IncludeGenerated { get; set; }

    /// <summary>Show external packages as nodes.</summary>
    [ObservableProperty]
    public partial bool IncludeExternalDependencies { get; set; }

    /// <summary>E.g. <c>2 filters active</c>; null when nothing beyond the defaults is filtered.</summary>
    [ObservableProperty]
    public partial string? FilterSummary { get; private set; }

    partial void OnSelectedMinLevelChanged(ChoiceOption value) => OnFilterOptionChanged();

    partial void OnIncludeTestsChanged(bool value) => OnFilterOptionChanged();

    partial void OnIncludeGeneratedChanged(bool value) => OnFilterOptionChanged();

    partial void OnIncludeExternalDependenciesChanged(bool value) => OnFilterOptionChanged();

    private void OnFilterOptionChanged()
    {
        if (!_suppressFilterChanges && _query is { } query)
        {
            _ = NavigateAsync(query with { Filters = CurrentFilters(), IncludeExternalDependencies = IncludeExternalDependencies });
        }
    }

    /// <summary>Clears every filter (tests and generated code hidden again, as by default).</summary>
    [RelayCommand]
    private Task ResetFiltersAsync()
    {
        _suppressFilterChanges = true;
        foreach (var option in LanguageFilters.Concat(EcosystemFilters).Concat(RelationshipKindFilters))
        {
            option.IsChecked = false;
        }

        SelectedMinLevel = MinLevelOptions[0];
        IncludeTests = false;
        IncludeGenerated = false;
        IncludeExternalDependencies = false;
        FilterSummary = null;
        _suppressFilterChanges = false;
        return _query is { } query
            ? NavigateAsync(query with { Filters = GraphFilters.Default, IncludeExternalDependencies = false })
            : Task.CompletedTask;
    }

    private GraphFilters CurrentFilters()
    {
        var filters = new GraphFilters
        {
            Languages = [.. LanguageFilters.Where(o => o.IsChecked).Select(o => o.Token)],
            Ecosystems = [.. EcosystemFilters.Where(o => o.IsChecked).Select(o => o.Token)],
            RelationshipKinds = [.. RelationshipKindFilters.Where(o => o.IsChecked).Select(o => o.Token)],
            MinLevel = SelectedMinLevel?.Token,
            IncludeTests = IncludeTests,
            IncludeGenerated = IncludeGenerated,
        };
        var active = filters.Languages.Count + filters.Ecosystems.Count + filters.RelationshipKinds.Count
            + (filters.MinLevel is null ? 0 : 1) + (IncludeTests ? 1 : 0) + (IncludeGenerated ? 1 : 0);
        FilterSummary = active == 0 ? null : active == 1 ? "1 filter setting changed" : $"{active} filter settings changed";
        return filters;
    }

    /// <summary>Offers the languages and ecosystems of the shown projects as filter values (kept while the repository stays).</summary>
    private void OfferFilterValues(GraphViewDto view)
    {
        _suppressFilterChanges = true;
        foreach (var language in view.Nodes.Where(n => n.Kind == GraphNodeKinds.Project).Select(n => n.Language).OfType<string>().Distinct().Order(StringComparer.Ordinal))
        {
            if (LanguageFilters.All(o => o.Token != language))
            {
                LanguageFilters.Add(new FilterOption(language, language, OnFilterOptionChanged));
            }
        }

        foreach (var ecosystem in view.Nodes.Where(n => n.Kind == GraphNodeKinds.Project).Select(n => n.Ecosystem).OfType<string>().Distinct().Order(StringComparer.Ordinal))
        {
            if (EcosystemFilters.All(o => o.Token != ecosystem))
            {
                EcosystemFilters.Add(new FilterOption(ecosystem, EcosystemName(ecosystem), OnFilterOptionChanged));
            }
        }

        _suppressFilterChanges = false;
    }

    private static string EcosystemName(string token) => token switch
    {
        "dotnet" => ".NET",
        "node" => "Node",
        "python" => "Python",
        _ => "Other",
    };

    private void ResetNavigation()
    {
        _suppressFilterChanges = true;
        LanguageFilters.Clear();
        EcosystemFilters.Clear();
        foreach (var option in RelationshipKindFilters)
        {
            option.IsChecked = false;
        }

        SelectedMinLevel = MinLevelOptions[0];
        IncludeTests = false;
        IncludeGenerated = false;
        IncludeExternalDependencies = false;
        FilterSummary = null;
        _suppressFilterChanges = false;
        Breadcrumbs.Clear();
        IsNeighborhood = false;
        FolderHint = null;
        SelectedViewMode = ViewModes[0];
        _history.Clear();
        CanGoBack = false;
        SearchText = null;
        SearchResults.Clear();
        SearchMessage = null;
    }

    // Neighborhood (WF-05)

    public IReadOnlyList<int> DepthOptions { get; } = [1, 2, 3];

    /// <summary>Hops shown around the focused element (1 … 3).</summary>
    [ObservableProperty]
    public partial int NeighborhoodDepth { get; set; } = 1;

    public IReadOnlyList<ChoiceOption> DirectionOptions { get; }

    [ObservableProperty]
    public partial ChoiceOption SelectedDirection { get; set; }

    /// <summary>True while the graph shows the neighborhood of one element instead of the whole graph.</summary>
    [ObservableProperty]
    public partial bool IsNeighborhood { get; private set; }

    /// <summary>The path to the focused element (repository first), GRAPH_ENGINE.md §3.</summary>
    public System.Collections.ObjectModel.ObservableCollection<BreadcrumbDto> Breadcrumbs { get; } = [];

    partial void OnNeighborhoodDepthChanged(int value) => OnNeighborhoodOptionChanged();

    partial void OnSelectedDirectionChanged(ChoiceOption value) => OnNeighborhoodOptionChanged();

    private void OnNeighborhoodOptionChanged()
    {
        if (!_suppressFilterChanges && _query is { Focus: not null } query)
        {
            _ = NavigateAsync(query with { Depth = NeighborhoodDepth, Direction = SelectedDirection.Token ?? GraphDirections.Both });
        }
    }

    /// <summary>Shows the neighborhood of the selected node.</summary>
    [RelayCommand]
    private Task ShowNeighborhoodAsync()
    {
        var selected = Graph.Selection.NodeIds.FirstOrDefault();
        return selected is null || _query is not { } query
            ? Task.CompletedTask
            : NavigateAsync(query with { Focus = selected, Depth = NeighborhoodDepth, Direction = SelectedDirection.Token ?? GraphDirections.Both });
    }

    /// <summary>Leaves the neighborhood view.</summary>
    [RelayCommand]
    private Task ShowWholeGraphAsync() =>
        _query is { Focus: not null } query ? NavigateAsync(query with { Focus = null }) : Task.CompletedTask;

    /// <summary>A breadcrumb: the repository leaves the neighborhood, any other step focuses that element.</summary>
    [RelayCommand]
    private Task OpenBreadcrumbAsync(BreadcrumbDto? crumb)
    {
        if (crumb is null || _query is not { } query)
        {
            return Task.CompletedTask;
        }

        return string.Equals(crumb.Id, query.RepositoryId, StringComparison.OrdinalIgnoreCase)
            ? NavigateAsync(query with { Focus = null })
            : NavigateAsync(query with { Focus = crumb.Id, Depth = NeighborhoodDepth, Direction = SelectedDirection.Token ?? GraphDirections.Both });
    }

    // Search (Ctrl/Cmd+K, WF-05)

    /// <summary>The search text; results update while typing.</summary>
    [ObservableProperty]
    public partial string? SearchText { get; set; }

    public System.Collections.ObjectModel.ObservableCollection<SearchResultItem> SearchResults { get; } = [];

    /// <summary>E.g. <c>12 results</c>, <c>50 of 340 results</c> or <c>No results</c>.</summary>
    [ObservableProperty]
    public partial string? SearchMessage { get; private set; }

    partial void OnSearchTextChanged(string? value) => _ = SearchAsync(value);

    private async Task SearchAsync(string? text)
    {
        var searchNumber = ++_searchNumber;
        if (string.IsNullOrWhiteSpace(text) || _query is not { } query)
        {
            SearchResults.Clear();
            SearchMessage = null;
            return;
        }

        try
        {
            var result = await _search.ExecuteAsync(query.RepositoryId, text, SearchElements.DefaultLimit, CancellationToken.None);
            if (searchNumber != _searchNumber)
            {
                return; // the text changed meanwhile
            }

            SearchResults.Clear();
            if (!result.TryGetValue(out var found, out var error))
            {
                SearchMessage = error.Message;
                return;
            }

            foreach (var item in found.Items)
            {
                SearchResults.Add(new SearchResultItem(item.Element.Label, item.KindText, item.Container, item.Element));
            }

            SearchMessage = found.TotalCount == 0 ? "No results"
                : found.TotalCount > found.Items.Count ? $"{found.Items.Count} of {found.TotalCount} results"
                : found.TotalCount == 1 ? "1 result" : $"{found.TotalCount} results";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Searching failed unexpectedly");
            SearchMessage = "The search failed. Details are in the application log.";
        }
    }

    /// <summary>Opens the neighborhood of a search result (WF-05 step 3) and selects it.</summary>
    [RelayCommand]
    private async Task OpenSearchResultAsync(SearchResultItem? item)
    {
        if (item is null || _query is not { } query)
        {
            return;
        }

        var expanded = query.Expanded.ToHashSet(StringComparer.Ordinal);
        expanded.UnionWith(item.Reference.ExpandPath);
        var focus = item.Reference.GraphNodeId ?? item.Reference.Id;
        var next = query with
        {
            Expanded = [.. expanded.Order(StringComparer.Ordinal)],
            Focus = focus,
            Depth = NeighborhoodDepth,
            Direction = SelectedDirection.Token ?? GraphDirections.Both,
            IncludeExternalDependencies = query.IncludeExternalDependencies || item.Reference.Kind == GraphNodeKinds.ExternalDependency,
        };

        if (await NavigateAsync(next))
        {
            SearchText = null;
            await Graph.FocusAsync(focus, CancellationToken.None);
        }
    }

    /// <summary>Loads the view for <paramref name="next"/> and replaces the shown graph (filters, neighborhood, search).</summary>
    private async Task<bool> NavigateAsync(GraphQuery next, bool remember = true)
    {
        var loadNumber = _loadNumber;
        try
        {
            var result = await _getGraphView.ExecuteAsync(next, CancellationToken.None);
            if (loadNumber != _loadNumber)
            {
                return false; // another repository was loaded meanwhile
            }

            if (!result.TryGetValue(out var view, out var error))
            {
                _logger.LogWarning("Changing the graph view failed with {ErrorCode}", error.Code);
                ErrorMessage = error.Message;
                return false;
            }

            ErrorMessage = null;
            if (remember && _query is { } previous)
            {
                Remember(previous);
            }

            _query = next;
            ShowTruncation(view);
            ShowNavigation(view, next);
            if (!await Graph.ShowGraphAsync(view, CancellationToken.None))
            {
                ErrorMessage = "The graph view did not accept the graph. Details are in the application log.";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Changing the graph view failed unexpectedly");
            ErrorMessage = "The graph could not be changed. Details are in the application log.";
            return false;
        }
    }

    private void ShowNavigation(GraphViewDto view, GraphQuery query)
    {
        Breadcrumbs.Clear();
        foreach (var crumb in view.Breadcrumbs)
        {
            Breadcrumbs.Add(crumb);
        }

        IsNeighborhood = query.Focus is not null;
        FolderHint = view.Level == GraphLevels.Folder
            ? "No projects were recognized in this repository, so the graph shows its folders. Double-click a folder to see its subfolders."
            : null;
    }

    // ---- Repository level, folder graph and Back (GRAPH-009) ----

    /// <summary>Project level or grouped by workspace/solution/ecosystem (L0).</summary>
    public IReadOnlyList<ChoiceOption> ViewModes { get; }

    [ObservableProperty]
    public partial ChoiceOption SelectedViewMode { get; set; }

    /// <summary>Shown when the repository has no projects and the graph shows folders (WF-01).</summary>
    [ObservableProperty]
    public partial string? FolderHint { get; private set; }

    /// <summary>True if <see cref="BackCommand"/> can restore an earlier view state.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    public partial bool CanGoBack { get; private set; }

    partial void OnSelectedViewModeChanged(ChoiceOption value)
    {
        if (!_suppressFilterChanges && _query is { } query && value.Token is { } level && level != query.Level)
        {
            _ = NavigateAsync(query with { Level = level });
        }
    }

    /// <summary>Restores the previous view state: level, expansions, filters and neighborhood (WF-03 step 5).</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private async Task BackAsync()
    {
        if (_history.Count == 0)
        {
            return;
        }

        var previous = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        CanGoBack = _history.Count > 0;
        ShowControlsOf(previous);
        if (!await NavigateAsync(previous, remember: false))
        {
            Remember(previous); // keep it for another try
        }
    }

    private void Remember(GraphQuery previous)
    {
        if (_history.Count > 0 && _history[^1] == previous)
        {
            return;
        }

        _history.Add(previous);
        if (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
        }

        CanGoBack = true;
    }

    /// <summary>Sets filter, neighborhood and view-mode controls to a restored query without triggering reloads.</summary>
    private void ShowControlsOf(GraphQuery query)
    {
        _suppressFilterChanges = true;
        foreach (var option in LanguageFilters)
        {
            option.IsChecked = query.Filters.Languages.Contains(option.Token);
        }

        foreach (var option in EcosystemFilters)
        {
            option.IsChecked = query.Filters.Ecosystems.Contains(option.Token);
        }

        foreach (var option in RelationshipKindFilters)
        {
            option.IsChecked = query.Filters.RelationshipKinds.Contains(option.Token);
        }

        SelectedMinLevel = MinLevelOptions.FirstOrDefault(o => o.Token == query.Filters.MinLevel) ?? MinLevelOptions[0];
        IncludeTests = query.Filters.IncludeTests;
        IncludeGenerated = query.Filters.IncludeGenerated;
        IncludeExternalDependencies = query.IncludeExternalDependencies;
        NeighborhoodDepth = query.Depth;
        SelectedDirection = DirectionOptions.FirstOrDefault(o => o.Token == query.Direction) ?? DirectionOptions[0];
        SelectedViewMode = ViewModes.FirstOrDefault(o => o.Token == query.Level) ?? ViewModes[0];
        _ = CurrentFilters(); // updates the summary
        _suppressFilterChanges = false;
    }

    private static string KindText(GraphNodeDto node)
    {
        var kind = node.Kind switch
        {
            GraphNodeKinds.Project => "Project",
            GraphNodeKinds.ExternalDependency => "External dependency",
            GraphNodeKinds.Aggregate => "Elements hidden by the node limit",
            GraphNodeKinds.Group => "Group of projects",
            GraphNodeKinds.Folder => "Folder",
            GraphNodeKinds.Module => "Module",
            GraphNodeKinds.Entity => node.Sublabel is { Length: > 0 } entityKind ? char.ToUpperInvariant(entityKind[0]) + entityKind[1..] : "Code element",
            _ => node.Kind,
        };

        return node.Badges.Contains(GraphBadges.Missing) ? $"{kind} (project file missing)" : kind;
    }
}
