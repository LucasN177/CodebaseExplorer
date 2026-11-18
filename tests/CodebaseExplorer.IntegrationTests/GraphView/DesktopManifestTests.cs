using System.Xml.Linq;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.IntegrationTests.GraphView;

/// <summary>
/// GRAPH-004 finding: without an application manifest that lists the supported Windows versions,
/// Avalonia cannot create the child window hosting the WebView ("Unable to create child window for
/// native control host") and the app crashes when the graph host is shown.
/// </summary>
public sealed class DesktopManifestTests
{
    private const string Windows10And11 = "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}";

    private static string DesktopProject => Path.Combine(TestRepositoryCatalog.RepositoryRoot, "src", "CodebaseExplorer.Desktop");

    [Fact]
    public void DesktopProject_UsesTheApplicationManifest()
    {
        var project = XDocument.Load(Path.Combine(DesktopProject, "CodebaseExplorer.Desktop.csproj"));

        Assert.Equal("app.manifest", project.Descendants("ApplicationManifest").Single().Value);
    }

    [Fact]
    public void ApplicationManifest_DeclaresWindows10And11AsSupported()
    {
        var manifest = XDocument.Load(Path.Combine(DesktopProject, "app.manifest"));

        Assert.Contains(
            manifest.Descendants().Where(e => e.Name.LocalName == "supportedOS"),
            e => (string?)e.Attribute("Id") == Windows10And11);
    }
}
