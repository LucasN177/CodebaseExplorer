using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Storage.Stores;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Tests.Stores;

/// <summary>AI-006: conversations, messages with citations and manifest, ordering and cascades.</summary>
public sealed class AiConversationStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _database = new();
    private readonly DomainSamples _samples = new();
    private readonly AiConversationStore _store;

    public AiConversationStoreTests() => _store = new AiConversationStore(new TestContextFactory(_database));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Messages_RoundTrip_WithCitationsAndManifest()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);
        var conversation = Conversation(Guid.NewGuid(), "Where are orders placed?", Now);
        var manifest = new AiContextManifest([new AiContextEntry("S1", AiContextEntry.SnippetKind, "search match", "src/a.cs", 1, 40, null, 120)], 5000, 120, 3);
        var citation = new AiCitation("[S1]", "S1", "src/a.cs", 1, 40, null, true, null);

        await _store.AppendAsync(conversation,
        [
            new AiMessage(Guid.NewGuid(), 0, AiRole.User, "Where are orders placed?", [], null, null, null, Now),
            new AiMessage(Guid.NewGuid(), 0, AiRole.Assistant, "In the handler [S1].", [citation], manifest, 900, 40, Now),
        ], Token);

        var messages = await _store.LoadMessagesAsync(conversation.Id, Token);
        Assert.Equal([0, 1], messages.Select(m => m.Ordinal));
        Assert.Equal(AiRole.Assistant, messages[1].Role);
        Assert.Equal(citation, Assert.Single(messages[1].Citations));
        Assert.Equal(manifest.Entries, messages[1].Manifest!.Entries);
        Assert.Equal((5000, 120, 3), (messages[1].Manifest!.BudgetTokens, messages[1].Manifest!.UsedTokens, messages[1].Manifest!.OmittedEntries));
        Assert.Equal((900, 40), (messages[1].PromptTokens, messages[1].CompletionTokens));
        Assert.Null(messages[0].Manifest);
        Assert.Equal(conversation, await _store.FindAsync(conversation.Id, Token));
    }

    [Fact]
    public async Task Appending_ContinuesOrdinals_AndMovesTheConversationUp()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);
        var first = Conversation(Guid.NewGuid(), "first", Now);
        var second = Conversation(Guid.NewGuid(), "second", Now.AddMinutes(1));
        await _store.AppendAsync(first, [Message(AiRole.User, "a"), Message(AiRole.Assistant, "b")], Token);
        await _store.AppendAsync(second, [Message(AiRole.User, "c")], Token);

        await _store.AppendAsync(first with { UpdatedAt = Now.AddMinutes(5), IsRemote = true }, [Message(AiRole.User, "d")], Token);

        Assert.Equal(["first", "second"], (await _store.ListAsync(_samples.Repository.Id, Token)).Select(c => c.Title));
        Assert.Equal([0, 1, 2], (await _store.LoadMessagesAsync(first.Id, Token)).Select(m => m.Ordinal));
        Assert.True((await _store.FindAsync(first.Id, Token))!.IsRemote);
    }

    [Fact]
    public async Task DeletingAConversation_DeletesItsMessages_AndTheRepositoryDeletesEverything()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);
        var first = Conversation(Guid.NewGuid(), "first", Now);
        var second = Conversation(Guid.NewGuid(), "second", Now);
        await _store.AppendAsync(first, [Message(AiRole.User, "a")], Token);
        await _store.AppendAsync(second, [Message(AiRole.User, "b")], Token);

        Assert.True(await _store.DeleteAsync(first.Id, Token));
        Assert.False(await _store.DeleteAsync(first.Id, Token));
        Assert.Equal(1L, await _database.ScalarAsync("SELECT count(*) FROM AIMessages;"));

        await using (var context = _database.CreateContext())
        {
            await context.Repositories.ExecuteDeleteAsync(Token);
        }

        Assert.Equal(0L, await _database.ScalarAsync("SELECT count(*) FROM AIConversations;"));
        Assert.Equal(0L, await _database.ScalarAsync("SELECT count(*) FROM AIMessages;"));
    }

    private AiConversation Conversation(Guid id, string title, DateTimeOffset at) =>
        new(id, _samples.Repository.Id, title, "ollama", "llama3.2:3b", false, at, at);

    private static AiMessage Message(AiRole role, string text) => new(Guid.NewGuid(), 0, role, text, [], null, null, null, Now);
}
