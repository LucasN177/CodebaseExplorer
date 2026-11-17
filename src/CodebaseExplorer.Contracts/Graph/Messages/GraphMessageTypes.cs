namespace CodebaseExplorer.Contracts.Graph.Messages;

/// <summary>Message types of the .NET ↔ web view protocol (GRAPH_ENGINE.md §6).</summary>
public static class GraphMessageTypes
{
    public const int ProtocolVersion = 1;

    // .NET → web view
    public const string GraphLoad = "graph/load";
    public const string GraphFocus = "graph/focus";
    public const string ViewTheme = "view/theme";
    public const string ViewLayout = "view/layout";

    // web view → .NET
    public const string Ready = "ready";
    public const string SelectionChanged = "selection/changed";
    public const string NodeExpandRequested = "node/expandRequested";
    public const string NodeCollapseRequested = "node/collapseRequested";
    public const string NodeOpenInspector = "node/openInspector";
    public const string Error = "error";
}
