using CodebaseExplorer.AI.Prompting;
using CodebaseExplorer.AI.Retrieval;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>
/// AI-004: facts-first retrieval and prompt building on the dotnet-layered fixture. The context for a given question and
/// model is deterministic (snapshots), and the manifest lists exactly what was sent.
/// </summary>
public sealed class PromptContextTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly NamedFixtureCopy _repository = new("dotnet-layered");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _repository.Dispose();
    }

    [Fact]
    public async Task RepositoryQuestion_MatchesTheSnapshot()
    {
        var stored = await ImportAndIndexAsync();
        const string Question = "How are orders placed and stored?";

        var context = await Retriever.RetrieveAsync(stored, Question, null, Token);
        var package = PromptBuilder.Build(Question, [], context, 8192);

        AssertMatchesSnapshot("prompt-repository.txt", package.Messages[^1].Text);
        Assert.Equal(PromptBuilder.SystemPrompt, package.Messages[0].Text);
        Assert.Equal(2, package.Messages.Count);
        Assert.Equal(package.Manifest.Entries.Count, package.Manifest.Entries.Select(e => e.Key).Distinct().Count());
        Assert.DoesNotContain(_repository.FullPath, package.Messages[^1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ElementQuestion_StartsWithTheSelectedElement_AndMatchesTheSnapshot()
    {
        var stored = await ImportAndIndexAsync();
        var handler = stored.Model.Entities.Single(e => e.Name == "PlaceOrderHandler" && e.ContainingEntityId is null);
        const string Question = "Explain this component.";

        var context = await Retriever.RetrieveAsync(stored, Question, handler.Id.ToString(), Token);
        var package = PromptBuilder.Build(Question, [], context, 8192);

        var first = package.Manifest.Entries[0];
        Assert.Equal("F1", first.Key);
        Assert.Equal(handler.Id.ToString(), first.ElementId);
        Assert.Contains(package.Manifest.Entries, e => e.Kind == AiContextEntry.SnippetKind
            && e.Path == "src/Shop.Application/Orders/PlaceOrderHandler.cs" && e.ElementId == handler.Id.ToString());
        AssertMatchesSnapshot("prompt-element.txt", package.Messages[^1].Text);
    }

    [Fact]
    public async Task SameInput_GivesTheSameContext()
    {
        var stored = await ImportAndIndexAsync();

        var first = PromptBuilder.Build("Which projects reference the domain?", [], await Retriever.RetrieveAsync(stored, "Which projects reference the domain?", null, Token), 4096);
        var second = PromptBuilder.Build("Which projects reference the domain?", [], await Retriever.RetrieveAsync(stored, "Which projects reference the domain?", null, Token), 4096);

        Assert.Equal(first.Messages[^1].Text, second.Messages[^1].Text);
        Assert.Equal(first.Manifest.Entries, second.Manifest.Entries);
    }

    [Fact]
    public async Task SmallBudget_KeepsFactsFirst_AndReportsOmissions()
    {
        var stored = await ImportAndIndexAsync();
        var context = await Retriever.RetrieveAsync(stored, "How are orders placed?", null, Token);

        var small = PromptBuilder.Build("How are orders placed?", [], context, 2048);
        var large = PromptBuilder.Build("How are orders placed?", [], context, 32768);

        Assert.True(small.Manifest.UsedTokens <= small.Manifest.BudgetTokens);
        Assert.True(small.Manifest.OmittedEntries > 0);
        Assert.True(large.Manifest.Entries.Count > small.Manifest.Entries.Count);
        Assert.Equal(AiContextEntry.FactKind, small.Manifest.Entries[0].Kind);
    }

    private ContextRetriever Retriever => _app.Services.GetRequiredService<ContextRetriever>();

    private async Task<StoredRepositoryModel> ImportAndIndexAsync()
    {
        var stored = await _repository.ImportAsync(_app.Services, Token);

        (await _app.Services.GetRequiredService<EnsureSearchIndex>().ExecuteAsync(stored, Token)).Value.ToString();
        return stored;
    }

    /// <summary>Compares with <c>AI/Snapshots/&lt;name&gt;</c>; a difference is written next to it as <c>.received.txt</c> (git-ignored).</summary>
    internal static void AssertMatchesSnapshot(string name, string actual)
    {
        var path = Path.Combine(TestRepositoryCatalog.RepositoryRoot, "tests", "CodebaseExplorer.IntegrationTests", "AI", "Snapshots", name);
        var expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;
        if (expected != actual)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(Path.ChangeExtension(path, ".received.txt"), actual);
        }

        Assert.Equal(expected, actual);
    }
}
