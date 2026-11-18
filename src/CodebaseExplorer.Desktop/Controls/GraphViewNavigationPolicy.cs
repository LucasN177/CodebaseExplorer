namespace CodebaseExplorer.Desktop.Controls;

/// <summary>
/// Decides which navigations the graph web view may perform: only the bundled page itself
/// (GRAPH_ENGINE.md §7, ADR-006 "Spike result"). Everything else — remote URLs, other local files,
/// <c>data:</c>/<c>javascript:</c> URLs — is cancelled by the host.
/// </summary>
public static class GraphViewNavigationPolicy
{
    /// <summary>The bundled page, copied to the application output by the Desktop project (GRAPH-003).</summary>
    public static Uri PageUri { get; } = new(Path.Combine(AppContext.BaseDirectory, "GraphView", "index.html"));

    /// <summary>
    /// True only for a local-file URI of <paramref name="page"/> without query (a fragment stays on the
    /// same document). Paths compare case-insensitively on Windows, where the file system does.
    /// </summary>
    public static bool IsAllowed(Uri? request, Uri page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (request is null || !request.IsAbsoluteUri || !request.IsFile || !page.IsFile || request.IsUnc
            || !string.IsNullOrEmpty(request.Query))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(
            Path.GetFullPath(request.LocalPath),
            Path.GetFullPath(page.LocalPath),
            comparison);
    }
}
