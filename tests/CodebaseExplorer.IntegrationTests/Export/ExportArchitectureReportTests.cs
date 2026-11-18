using System.Text.RegularExpressions;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Export;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Export;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.IntegrationTests.Infrastructure;

using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Export;

/// <summary>
/// EXPORT-001: the Markdown/Mermaid report through the real composition root. The import and the report use a fixed
/// clock and a copy of the fixture outside any Git repository, so the report is byte-stable (snapshot test).
/// </summary>
public sealed partial class ExportArchitectureReportTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly TestApplication _app = new();
    private readonly SettableClock _clock = new(Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Report_ForDotnetLayered_MatchesTheSnapshot()
    {
        using var copy = new TempDirectory();
        var root = CopyFixture(copy, "dotnet-layered");
        var summary = await ImportAsync(root);

        var report = (await Export().ExecuteAsync(new ArchitectureReportRequest { RepositoryId = summary.RepositoryId }, Ct)).Value;

        Assert.Equal("ARCHITECTURE-dotnet-layered-2026-11-18.md", report.FileName);
        Assert.Equal("dotnet-layered", report.RepositoryName);
        Assert.False(report.GraphTruncated);
        Assert.DoesNotContain(copy.FullPath, report.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(root, report.Markdown, StringComparison.OrdinalIgnoreCase);
        AssertMatchesSnapshot("dotnet-layered.md", report.Markdown);
        AssertValidMermaid(report.Markdown);
    }

    [Fact]
    public async Task Report_WithHostileProjectNames_StaysValidMarkdownAndMermaid()
    {
        // Package names in package.json may contain anything, unlike file names: the hardest input for both escapers.
        using var repository = new TempDirectory();
        repository.WriteText("package.json", """{ "name": "root", "private": true, "workspaces": ["packages/*"] }""");
        repository.WriteText("packages/evil/package.json", """
            { "name": "Evil\"] --> n1 & <b>[x]</b> #quot; (a) {b} %% c `d` *e* _f_ ~g~ ;h | i\nj ![img](http://x)", "dependencies": { "lib": "*" } }
            """);
        repository.WriteText("packages/lib/package.json", """{ "name": "lib" }""");
        var summary = await ImportAsync(repository.FullPath);

        var markdown = (await Export().ExecuteAsync(new ArchitectureReportRequest { RepositoryId = summary.RepositoryId }, Ct)).Value.Markdown;

        AssertValidMermaid(markdown);
        Assert.Contains("[\"Evil#34;#93; --#62; n1 #38; #60;b#62;#91;x#93;#60;/b#62; #35;quot#59; #40;a#41; #123;b#125; %% c #96;d#96; #42;e#42; #95;f#95; #126;g#126; #59;h #124; i j !#91;img#93;#40;http://x#41;\"]",
            markdown, StringComparison.Ordinal);
        Assert.Matches(@"\n  n\d+ --> n\d+\n", markdown); // the dependency evil → lib is drawn
        var lines = markdown.Split('\n');
        Assert.DoesNotContain(lines, l => l.Contains("<b>", StringComparison.Ordinal) || l.Contains("![img]", StringComparison.Ordinal));
        // After "## Projects": blank line, header, separator, then one row per project (root, evil, lib).
        var projectRows = lines.SkipWhile(l => l != "## Projects").Skip(4).TakeWhile(l => l.StartsWith('|')).ToList();
        Assert.Equal(3, projectRows.Count);
        Assert.All(projectRows, row => Assert.Equal(9, UnescapedPipes(row)));
        Assert.Contains(projectRows, row => row.StartsWith(@"| Evil""\]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Report_OfTheCurrentView_DrawsItsExpansionsAsSubgraphs()
    {
        using var copy = new TempDirectory();
        var root = CopyFixture(copy, "dotnet-layered");
        var summary = await ImportAsync(root);
        var model = (await _app.Services.GetRequiredService<IArchitectureModelStore>()
            .LoadCurrentModelAsync(new Domain.Common.RepositoryId(Guid.ParseExact(summary.RepositoryId, "N")), Ct))!.Model;
        var domain = model.Projects.Single(p => p.Name == "Shop.Domain").Id.ToString();

        var report = (await Export().ExecuteAsync(new ArchitectureReportRequest
        {
            RepositoryId = summary.RepositoryId,
            Sections = [ReportSections.DependencyGraph, ReportSections.Overview],
            View = new GraphQuery { RepositoryId = summary.RepositoryId, Expanded = [domain] },
        }, Ct)).Value;

        Assert.Contains("Scope: current graph view.", report.Markdown, StringComparison.Ordinal);
        Assert.Matches(@"\n  subgraph n\d+ \[""Shop\.Domain""\]\n", report.Markdown);
        Assert.True(report.Markdown.IndexOf("## Overview", StringComparison.Ordinal) < report.Markdown.IndexOf("## Dependency graph", StringComparison.Ordinal),
            "Sections follow the report order, not the request order.");
        Assert.DoesNotContain("## Languages", report.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Shop.Tests", report.Markdown, StringComparison.Ordinal); // hidden by the view's default filters
        AssertValidMermaid(report.Markdown);
    }

    [Theory]
    [InlineData(new string[0], ExportErrorCodes.NoSections)]
    [InlineData(new[] { "overview", "secrets" }, ExportErrorCodes.SectionUnknown)]
    public async Task InvalidSections_AreRejected(string[] sections, string code)
    {
        var result = await Export().ExecuteAsync(new ArchitectureReportRequest { RepositoryId = new string('a', 32), Sections = sections }, Ct);

        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public async Task UnknownRepository_IsNotAnalyzed()
    {
        var result = await Export().ExecuteAsync(new ArchitectureReportRequest { RepositoryId = new string('a', 32) }, Ct);

        Assert.Equal(RepositoryErrorCodes.NotAnalyzed, result.Error.Code);
    }

    [Fact]
    public void DependencyInjection_RegistersTheUseCase() =>
        Assert.NotNull(_app.Services.GetRequiredService<ExportArchitectureReport>());

    [Theory]
    [InlineData("Shop", "Shop")]
    [InlineData("my repo: v2/beta", "my-repo--v2-beta")]
    [InlineData("..\\..\\evil", "evil")]
    [InlineData("???", "repository")]
    [InlineData("Ünïcode", "n-code")]
    public void FileNamePart_IsSafe(string name, string expected) =>
        Assert.Equal(expected, ExportArchitectureReport.FileNamePart(name));

    /// <summary>Every line of every Mermaid block follows the small grammar the renderer emits; labels contain no raw markup.</summary>
    private static void AssertValidMermaid(string markdown)
    {
        var blocks = MermaidBlock().Matches(markdown);
        Assert.NotEmpty(blocks);
        foreach (Match block in blocks)
        {
            var lines = block.Groups[1].Value.TrimEnd('\n').Split('\n');
            Assert.Equal("flowchart LR", lines[0]);
            var depth = 0;
            foreach (var line in lines.Skip(1))
            {
                var node = NodeLine().Match(line);
                var subgraph = SubgraphLine().Match(line);
                Assert.True(node.Success || subgraph.Success || EndLine().IsMatch(line) || EdgeLine().IsMatch(line), $"Invalid Mermaid line: {line}");
                depth += subgraph.Success ? 1 : EndLine().IsMatch(line) ? -1 : 0;
                Assert.True(depth >= 0, "end without subgraph");
                var label = node.Success ? node.Groups[1].Value : subgraph.Success ? subgraph.Groups[1].Value : string.Empty;
                Assert.DoesNotMatch(@"[""#&<>`|\[\]{}();\\*_~]", Entity().Replace(label, string.Empty));
            }

            Assert.Equal(0, depth);
        }
    }

    /// <summary>Copies a fixture into <c>&lt;temp&gt;/&lt;fixture&gt;</c>, so the repository keeps the fixture's name.</summary>
    private static string CopyFixture(TempDirectory temp, string fixture)
    {
        var source = TestRepositoryCatalog.GetFixturePath(fixture);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = temp.PathOf($"{fixture}/{Path.GetRelativePath(source, file).Replace('\\', '/')}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return temp.PathOf(fixture);
    }

    private static int UnescapedPipes(string row) => Regex.Matches(row, @"(?<!\\)\|").Count;

    private ExportArchitectureReport Export() =>
        new(_app.Services.GetRequiredService<IArchitectureModelStore>(), _app.Services.GetRequiredService<IGraphProjector>(), _clock);

    private async Task<RepositoryImportSummaryDto> ImportAsync(string path)
    {
        var import = new ImportLocalRepository(
            _app.Services.GetRequiredService<IRepositoryRootValidator>(),
            _app.Services.GetRequiredService<IRepositoryAnalyzer>(),
            _app.Services.GetRequiredService<IArchitectureModelStore>(),
            _app.Services.GetRequiredService<IGitRepositoryReader>(),
            _app.Services.GetRequiredService<IGitActivityStore>(),
            _clock);
        var result = await import.ExecuteAsync(path, null, Ct);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    /// <summary>
    /// Compares with <c>Export/Snapshots/&lt;name&gt;</c> in the source tree. On a difference the actual report is written next
    /// to it as <c>&lt;name&gt;.received.md</c> (git-ignored) for review; accept it by replacing the snapshot.
    /// </summary>
    private static void AssertMatchesSnapshot(string name, string actual)
    {
        var path = Path.Combine(TestRepositoryCatalog.RepositoryRoot, "tests", "CodebaseExplorer.IntegrationTests", "Export", "Snapshots", name);
        var expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
        if (expected != actual)
        {
            File.WriteAllText(Path.ChangeExtension(path, ".received.md"), actual);
        }

        Assert.Equal(expected, actual);
    }

    [GeneratedRegex("```mermaid\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex MermaidBlock();

    [GeneratedRegex(@"^\s+n\d+\[""([^""\n]*)""\]$")]
    private static partial Regex NodeLine();

    [GeneratedRegex(@"^\s+subgraph n\d+ \[""([^""\n]*)""\]$")]
    private static partial Regex SubgraphLine();

    [GeneratedRegex(@"^\s+end$")]
    private static partial Regex EndLine();

    [GeneratedRegex(@"^\s+n\d+ (-->|-\.->)(\|""\d+""\|)? n\d+$")]
    private static partial Regex EdgeLine();

    [GeneratedRegex(@"#\d+;")]
    private static partial Regex Entity();
}
