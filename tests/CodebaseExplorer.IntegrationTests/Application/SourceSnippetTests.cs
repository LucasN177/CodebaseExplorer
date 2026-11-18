using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Application.Source;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.IntegrationTests.Security;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>
/// UI-007: source snippets around evidence locations, read through the containment-checking file reader; missing and
/// changed files are reported, not crashed. Uses a copy of <c>dotnet-layered</c>, so files can be changed.
/// </summary>
public sealed class SourceSnippetTests : IAsyncLifetime
{
    private const string Infrastructure = "src/Shop.Infrastructure/Shop.Infrastructure.csproj";

    private readonly TestApplication _app = new();
    private readonly TempDirectory _repository = ZeroExecutionHarness.CopyFixture("dotnet-layered");
    private RepositoryImportSummaryDto _imported = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private GetSourceSnippet Snippets => _app.Services.GetRequiredService<GetSourceSnippet>();

    public async ValueTask InitializeAsync()
    {
        await _app.InitializeAsync();
        _imported = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(_repository.FullPath, null, Token)).Value;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _repository.Dispose();
    }

    [Fact]
    public async Task Snippet_ShowsTheExactRangeWithFiveLinesOfContext()
    {
        // The missing-project warning of EXPECTED.md §4 is at line 6.
        var snippet = (await Snippets.ExecuteAsync(_imported.RepositoryId, Infrastructure, 6, 6, Token)).Value;

        var fileLines = File.ReadAllLines(_repository.PathOf(Infrastructure));
        Assert.Equal(Enumerable.Range(1, 11), snippet.Lines.Select(l => l.Number));
        Assert.Equal(fileLines[..11], snippet.Lines.Select(l => l.Text));
        Assert.Equal([6], snippet.Lines.Where(l => l.IsEvidence).Select(l => l.Number));
        Assert.False(snippet.ChangedSinceAnalysis);
        Assert.False(snippet.RangeTruncated);
        Assert.Equal(_repository.PathOf(Infrastructure), snippet.AbsolutePath);
    }

    [Fact]
    public async Task MultiLineRange_IsHighlighted_AndContextIsClampedToTheFile()
    {
        var path = "src/Shop.Domain/Orders/OrderLine.cs"; // 3 lines
        var snippet = (await Snippets.ExecuteAsync(_imported.RepositoryId, path, 1, 2, Token)).Value;

        Assert.Equal([1, 2, 3], snippet.Lines.Select(l => l.Number));
        Assert.Equal([1, 2], snippet.Lines.Where(l => l.IsEvidence).Select(l => l.Number));
    }

    [Fact]
    public async Task ChangedFile_IsShownAndFlagged()
    {
        File.AppendAllText(_repository.PathOf(Infrastructure), "<!-- changed -->\n");

        var snippet = (await Snippets.ExecuteAsync(_imported.RepositoryId, Infrastructure, 6, null, Token)).Value;

        Assert.True(snippet.ChangedSinceAnalysis);
        Assert.Contains(snippet.Lines, l => l.Number == 6 && l.IsEvidence);
    }

    [Fact]
    public async Task ShortenedFile_ReportsMissingLines_OrATruncatedRange()
    {
        File.WriteAllText(_repository.PathOf(Infrastructure), "<Project>\n</Project>\n");

        var outside = await Snippets.ExecuteAsync(_imported.RepositoryId, Infrastructure, 6, null, Token);
        var partly = (await Snippets.ExecuteAsync(_imported.RepositoryId, Infrastructure, 2, 4, Token)).Value;

        Assert.Equal(SourceErrorCodes.LinesMissing, outside.Error.Code);
        Assert.True(partly.RangeTruncated);
        Assert.Equal([1, 2], partly.Lines.Select(l => l.Number));
    }

    [Fact]
    public async Task DeletedFile_AndRemovedFolder_AreReported()
    {
        File.Delete(_repository.PathOf(Infrastructure));
        var missingFile = await Snippets.ExecuteAsync(_imported.RepositoryId, Infrastructure, 6, null, Token);

        Directory.Delete(_repository.FullPath, recursive: true);
        var missingFolder = await Snippets.ExecuteAsync(_imported.RepositoryId, "src/Shop.Domain/Orders/Order.cs", 1, null, Token);

        Assert.Equal(SourceErrorCodes.FileMissing, missingFile.Error.Code);
        Assert.Equal(SourceErrorCodes.FolderMissing, missingFolder.Error.Code);
    }

    [Theory]
    [InlineData("../outside.txt", 1, null, SourceErrorCodes.FileNotInModel)]
    [InlineData("src/Shop.Web/NotThere.cs", 1, null, SourceErrorCodes.FileNotInModel)]
    [InlineData(Infrastructure, 0, null, SourceErrorCodes.RangeInvalid)]
    [InlineData(Infrastructure, 5, 4, SourceErrorCodes.RangeInvalid)]
    public async Task InvalidRequests_AreRejected(string path, int startLine, int? endLine, string code)
    {
        Assert.Equal(code, (await Snippets.ExecuteAsync(_imported.RepositoryId, path, startLine, endLine, Token)).Error.Code);
    }
}
