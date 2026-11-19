using CodebaseExplorer.AI.Providers;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Contracts.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>
/// AI-006 end to end through the real composition root: question → index → retrieval → stub Ollama (streaming) →
/// citation validation → stored conversation. Failures and cancellation store nothing.
/// </summary>
public sealed class AskRepositoryQuestionTests : IAsyncLifetime
{
    private readonly StubOllamaServer _server = new();
    private readonly TestApplication _app = new();
    private readonly NamedFixtureCopy _repository = new("dotnet-layered");
    private string _repositoryId = string.Empty;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private AskRepositoryQuestion Ask => _app.Services.GetRequiredService<AskRepositoryQuestion>();

    public async ValueTask InitializeAsync()
    {
        await _app.InitializeAsync();
        _repositoryId = (await _repository.ImportAsync(_app.Services, Token)).Repository.Id.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _server.DisposeAsync();
        _repository.Dispose();
    }

    [Fact]
    public async Task Answer_IsStreamed_Validated_AndStored()
    {
        await EnableAsync(_server.Endpoint);
        _server.AnswerChunks = ["Orders are placed by ", "`PlaceOrderHandler` [F1] [S99]."];
        var streamed = new List<string>();

        var exchange = (await Ask.ExecuteAsync(_repositoryId, null, "How are orders placed?", null, new SynchronousProgress<string>(streamed.Add), Token)).Value;

        Assert.Equal(["Orders are placed by ", "`PlaceOrderHandler` [F1] [S99]."], streamed);
        Assert.Equal("Orders are placed by `PlaceOrderHandler` [F1] [S99].", exchange.Answer.Content);
        Assert.Equal(AiMessageDto.AssistantRole, exchange.Answer.Role);
        Assert.Equal([("[F1]", true), ("[S99]", false)], exchange.Answer.Citations.Select(c => (c.Text, c.Verified)));
        Assert.NotEmpty(exchange.Answer.Context);
        Assert.Equal((120, 7), (exchange.Answer.PromptTokens, exchange.Answer.CompletionTokens));
        Assert.Equal("How are orders placed?", exchange.Conversation.Title);
        Assert.False(exchange.Conversation.IsRemote);

        var conversations = _app.Services.GetRequiredService<AiConversations>();
        var stored = (await conversations.GetMessagesAsync(exchange.Conversation.Id, Token)).Value;
        Assert.Equal(["How are orders placed?", exchange.Answer.Content], stored.Select(m => m.Content));
        Assert.Equal(exchange.Answer.Citations, stored[1].Citations);
        Assert.Single((await conversations.ListAsync(_repositoryId, Token)).Value);

        var request = Assert.Single(_server.ChatRequests).RootElement;
        Assert.Equal("llama3.2:3b", request.GetProperty("model").GetString());
        Assert.Equal(AiSettings.DefaultMaxContextTokens, request.GetProperty("options").GetProperty("num_ctx").GetInt32());
        var messages = request.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.StartsWith("<context>", messages[messages.GetArrayLength() - 1].GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.False(request.TryGetProperty("tools", out var tools) && tools.ValueKind == System.Text.Json.JsonValueKind.Array && tools.GetArrayLength() > 0);
    }

    [Fact]
    public async Task FollowUpQuestion_SendsTheHistory_AndContinuesTheConversation()
    {
        await EnableAsync(_server.Endpoint);
        var first = (await Ask.ExecuteAsync(_repositoryId, null, "What does the domain contain?", null, null, Token)).Value;

        var second = (await Ask.ExecuteAsync(_repositoryId, first.Conversation.Id, "And the application?", null, null, Token)).Value;

        Assert.Equal(first.Conversation.Id, second.Conversation.Id);
        Assert.Equal(2, second.Question.Ordinal);
        var messages = _server.ChatRequests.Last().RootElement.GetProperty("messages");
        Assert.Equal(4, messages.GetArrayLength()); // system, earlier question, earlier answer, new question with context
        Assert.Equal("What does the domain contain?", messages[1].GetProperty("content").GetString());
        Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Disabled_RemoteWithoutConsent_AndProviderFailures_StoreNothing()
    {
        var conversations = _app.Services.GetRequiredService<AiConversations>();

        Assert.Equal(AiErrorCodes.Disabled, (await Ask.ExecuteAsync(_repositoryId, null, "q?", null, null, Token)).Error.Code);

        await EnableAsync("http://gpu-box.invalid:11434");
        var consent = await Ask.ExecuteAsync(_repositoryId, null, "q?", null, null, Token);
        Assert.Equal(AiErrorCodes.ConsentRequired, consent.Error.Code);

        await EnableAsync(_server.Endpoint);
        _server.FailWith = System.Net.HttpStatusCode.NotFound;
        Assert.Equal(AiErrorCodes.ModelNotFound, (await Ask.ExecuteAsync(_repositoryId, null, "q?", null, null, Token)).Error.Code);

        _server.FailWith = System.Net.HttpStatusCode.InternalServerError;
        Assert.Equal(AiErrorCodes.RequestFailed, (await Ask.ExecuteAsync(_repositoryId, null, "q?", null, null, Token)).Error.Code);

        Assert.Empty((await conversations.ListAsync(_repositoryId, Token)).Value);
    }

    [Fact]
    public async Task Cancelling_StopsTheAnswer_AndStoresNothing()
    {
        await EnableAsync(_server.Endpoint);
        _server.AnswerChunks = ["a", "b", "c", "d"];
        _server.ChunkDelay = TimeSpan.FromMilliseconds(300);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Ask.ExecuteAsync(_repositoryId, null, "q?", null, new SynchronousProgress<string>(_ => cancel.Cancel()), cancel.Token));

        Assert.Empty((await _app.Services.GetRequiredService<AiConversations>().ListAsync(_repositoryId, Token)).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQuestions_AreRejected(string question) =>
        Assert.Equal(AiErrorCodes.QuestionInvalid, (await Ask.ExecuteAsync(_repositoryId, null, question, null, null, Token)).Error.Code);

    private async Task EnableAsync(string endpoint) =>
        await _app.Services.GetRequiredService<AiSettingsStore>().SaveAsync(
            AiSettings.Default with { Enabled = true, Endpoint = endpoint, ModelId = "llama3.2:3b" }, Token);
}

/// <summary>Reports on the calling thread (unlike <see cref="Progress{T}"/>), so tests see every piece in order.</summary>
internal sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
