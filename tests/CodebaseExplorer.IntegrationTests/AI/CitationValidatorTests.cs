using CodebaseExplorer.AI.Citations;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Repositories;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>The dotnet-layered model, imported once for all citation tests.</summary>
public sealed class ImportedLayeredRepository : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly NamedFixtureCopy _copy = new("dotnet-layered");

    internal StoredRepositoryModel Stored { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _app.InitializeAsync();
        Stored = await _copy.ImportAsync(_app.Services, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _copy.Dispose();
    }
}

/// <summary>AI-005: valid, invalid, out-of-range and fabricated citations against the dotnet-layered model.</summary>
public sealed class CitationValidatorTests(ImportedLayeredRepository repository) : IClassFixture<ImportedLayeredRepository>
{
    private const string Handler = "src/Shop.Application/Orders/PlaceOrderHandler.cs";

    private AiContextManifest Manifest => new(
    [
        new AiContextEntry("F1", AiContextEntry.FactKind, "fact", "src/Shop.Application/Shop.Application.csproj", 4, 4,
            repository.Stored.Model.Projects[0].Id.ToString(), 10),
        new AiContextEntry("S1", AiContextEntry.SnippetKind, "search match", Handler, 1, 13, null, 50),
        new AiContextEntry("F2", AiContextEntry.FactKind, "stale", null, null, null, "0123456789abcdef0123456789abcdef", 5),
    ], 1000, 65, 0);

    private IReadOnlyList<AiCitation> Validate(string answer) => CitationValidator.Validate(answer, Manifest, repository.Stored.Model);

    [Fact]
    public void KeysOfTheContext_AreVerified_AndResolveToTheirLocation()
    {
        var citations = Validate("The handler [S1] lives in Shop.Application [F1].");

        Assert.Equal(["[S1]", "[F1]"], citations.Select(c => c.Text));
        Assert.All(citations, c => Assert.True(c.Verified, c.Problem));
        Assert.Equal((Handler, 1, 13), (citations[0].Path, citations[0].StartLine, citations[0].EndLine));
        Assert.Equal(repository.Stored.Model.Projects[0].Id.ToString(), citations[1].ElementId);
    }

    [Fact]
    public void GroupedAndLowercaseKeys_AreFound_OnceEach()
    {
        var citations = Validate("See [F1, s1] and again [S1] and [f1].");

        Assert.Equal(["[F1]", "[S1]"], citations.Select(c => c.Text));
    }

    [Fact]
    public void FabricatedKey_IsUnverified()
    {
        var citation = Assert.Single(Validate("The repository uses Kafka [S9]."));

        Assert.False(citation.Verified);
        Assert.Equal("S9", citation.Key);
        Assert.Contains("not part of the context", citation.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyOfAnElementMissingFromTheModel_IsUnverified() =>
        Assert.False(Assert.Single(Validate("Old fact [F2].")).Verified);

    [Theory]
    [InlineData("`src/Shop.Application/Orders/PlaceOrderHandler.cs:5-13`", true, 5, 13)]
    [InlineData("src/Shop.Application/Orders/PlaceOrderHandler.cs:7", true, 7, 7)]
    [InlineData("PlaceOrderHandler.cs:5–8", true, 5, 8)]
    [InlineData("./src/Shop.Application/Orders/PlaceOrderHandler.cs:1-2", true, 1, 2)]
    public void PathCitationsWithinTheFile_AreVerified(string text, bool verified, int start, int end)
    {
        var citation = Assert.Single(Validate($"See {text}."));

        Assert.Equal(verified, citation.Verified);
        Assert.Equal((Handler, start, end), (citation.Path, citation.StartLine, citation.EndLine));
    }

    [Theory]
    [InlineData("src/Shop.Application/Orders/PlaceOrderHandler.cs:10-400", "only")]
    [InlineData("src/Shop.Application/Orders/PlaceOrderHandler.cs:9-3", "not valid")]
    [InlineData("src/Shop.Application/Orders/PlaceOrderHandler.cs:0", "not valid")]
    [InlineData("src/Shop.Billing/Invoice.cs:1-5", "not part of the analyzed repository")]
    public void PathCitationsOutsideTheModel_AreUnverified(string text, string problem)
    {
        var citation = Assert.Single(Validate($"Look at {text}"));

        Assert.False(citation.Verified);
        Assert.Contains(problem, citation.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("The server listens on localhost:11434 and example.com:443.")]
    [InlineData("It took 10:30 minutes; version 1.2:3 is unrelated.")]
    [InlineData("No citations here.")]
    public void TextThatIsNotACitation_IsIgnored(string answer) => Assert.Empty(Validate(answer));
}
