using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.IntegrationTests.GraphView;

/// <summary>
/// Guards the committed graph web view bundle (GRAPH-003): it must exist, carry the strict CSP, be
/// copied to the Desktop output, and be built from the current sources (no stale bundle).
/// </summary>
public sealed class GraphViewAssetsTests
{
    private const string StrictCsp =
        "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'none'";

    /// <summary>Mirrors HASHED_ROOTS in src/CodebaseExplorer.GraphView.Web/buildInfo.ts.</summary>
    private static readonly string[] HashedRoots =
        ["src", "public", "package.json", "package-lock.json", "tsconfig.json", "vite.config.ts", "buildInfo.ts"];

    private static string WebProject => Path.Combine(TestRepositoryCatalog.RepositoryRoot, "src", "CodebaseExplorer.GraphView.Web");

    private static string Bundle => Path.Combine(TestRepositoryCatalog.RepositoryRoot, "src", "CodebaseExplorer.Desktop", "GraphView");

    [Fact]
    public void Bundle_ContainsPageScriptAndBuildInfo()
    {
        Assert.True(File.Exists(Path.Combine(Bundle, "index.html")));
        Assert.True(new FileInfo(Path.Combine(Bundle, "graph.js")).Length > 100_000, "graph.js should contain the Cytoscape bundle");
        Assert.True(File.Exists(Path.Combine(Bundle, "build-info.json")));
    }

    [Fact]
    public void Page_HasStrictCspAndLoadsOnlyTheLocalClassicScript()
    {
        var html = File.ReadAllText(Path.Combine(Bundle, "index.html"));

        Assert.Contains($"content=\"{StrictCsp}\"", html, StringComparison.Ordinal);
        Assert.Contains("<script src=\"graph.js\"></script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"module\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Bundle_IsBuiltFromTheCurrentSources()
    {
        using var buildInfo = JsonDocument.Parse(File.ReadAllText(Path.Combine(Bundle, "build-info.json")));
        var committedHash = buildInfo.RootElement.GetProperty("sourceHash").GetString();

        Assert.True(
            committedHash == SourceHash(WebProject),
            "The committed graph bundle is stale. Run `npm ci && npm run build` in src/CodebaseExplorer.GraphView.Web and commit the result.");
    }

    [Fact]
    public void Bundle_IsCopiedToTheDesktopOutput()
    {
        // IntegrationTests references Desktop, so Desktop's content files are copied here as well.
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "GraphView", "index.html")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "GraphView", "graph.js")));
    }

    /// <summary>
    /// Same algorithm as sourceHash() in buildInfo.ts: SHA-256 over
    /// <c>path + "\n" + content(LF) + "\n"</c> of all hashed files, ordered by path (ordinal).
    /// </summary>
    private static string SourceHash(string root)
    {
        var files = HashedRoots
            .SelectMany(entry =>
            {
                var path = Path.Combine(root, entry);
                return Directory.Exists(path)
                    ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    : [path];
            })
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            var content = File.ReadAllText(Path.Combine(root, file), Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal);
            hash.AppendData(Encoding.UTF8.GetBytes($"{file}\n{content}\n"));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
