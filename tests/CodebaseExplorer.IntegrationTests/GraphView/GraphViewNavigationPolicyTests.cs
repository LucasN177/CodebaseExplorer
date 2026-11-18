using CodebaseExplorer.Desktop.Controls;

namespace CodebaseExplorer.IntegrationTests.GraphView;

/// <summary>GRAPH-004: the graph web view may navigate to the bundled page only.</summary>
public sealed class GraphViewNavigationPolicyTests
{
    private static readonly Uri Page = GraphViewNavigationPolicy.PageUri;

    [Fact]
    public void PageUri_PointsToTheBundledPageInTheOutput()
    {
        Assert.True(Page.IsFile);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "GraphView", "index.html"), Page.LocalPath);
        Assert.True(File.Exists(Page.LocalPath));
    }

    [Fact]
    public void BundledPage_IsAllowed()
    {
        Assert.True(GraphViewNavigationPolicy.IsAllowed(Page, Page));
        Assert.True(GraphViewNavigationPolicy.IsAllowed(new Uri(Page.AbsoluteUri + "#graph"), Page));
    }

    [Fact]
    public void BundledPage_DifferentCase_IsAllowedOnlyOnWindows()
    {
        var upper = new Uri(Page.LocalPath.ToUpperInvariant());

        Assert.Equal(OperatingSystem.IsWindows(), GraphViewNavigationPolicy.IsAllowed(upper, Page));
    }

    public static TheoryData<string> BlockedUris => new()
    {
        "https://example.com/",
        "http://localhost:5000/index.html",
        "about:blank",
        "data:text/html;base64,PGgxPmhpPC9oMT4=",
        "javascript:alert(1)",
        "ftp://example.com/index.html",
        "file://server/share/GraphView/index.html",
    };

    [Theory]
    [MemberData(nameof(BlockedUris))]
    public void OtherUris_AreBlocked(string uri) =>
        Assert.False(GraphViewNavigationPolicy.IsAllowed(new Uri(uri), Page));

    [Fact]
    public void OtherLocalFiles_AndQueries_AreBlocked()
    {
        var directory = Path.GetDirectoryName(Page.LocalPath)!;

        Assert.False(GraphViewNavigationPolicy.IsAllowed(new Uri(Path.Combine(directory, "graph.js")), Page));
        Assert.False(GraphViewNavigationPolicy.IsAllowed(new Uri(Path.Combine(directory, "..", "index.html")), Page));
        Assert.False(GraphViewNavigationPolicy.IsAllowed(new Uri(Page.AbsoluteUri + "?x=1"), Page));
        Assert.False(GraphViewNavigationPolicy.IsAllowed(null, Page));
        Assert.False(GraphViewNavigationPolicy.IsAllowed(new Uri("index.html", UriKind.Relative), Page));
    }
}
