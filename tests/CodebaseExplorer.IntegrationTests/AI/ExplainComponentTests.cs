using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Inspector;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>
/// AI-008: component explanations are generated from the element's context, validated, stored as a conversation and
/// cached per analysis run; a new run or "regenerate" asks the model again.
/// </summary>
public sealed class ExplainComponentTests : IAsyncLifetime
{
    private readonly StubOllamaServer _server = new();
    private readonly TestApplication _app = new();
    private readonly NamedFixtureCopy _repository = new("dotnet-layered");
    private StoredRepositoryModel _stored = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ExplainComponent Explain => _app.Services.GetRequiredService<ExplainComponent>();

    private string RepositoryId => _stored.Repository.Id.ToString();

    private string HandlerId => _stored.Model.Entities.Single(e => e.Name == "PlaceOrderHandler" && e.ContainingEntityId is null).Id.ToString();

    public async ValueTask InitializeAsync()
    {
        await _app.InitializeAsync();
        _stored = await _repository.ImportAsync(_app.Services, Token);
        await _app.Services.GetRequiredService<AiSettingsStore>().SaveAsync(
            AiSettings.Default with { Enabled = true, Endpoint = _server.Endpoint, ModelId = "llama3.2:3b" }, Token);
        _server.AnswerChunks = ["It handles placing orders [F1] and stores them through the repository [S1]."];
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _server.DisposeAsync();
        _repository.Dispose();
    }

    [Fact]
    public async Task Explanation_IsGeneratedFromTheElement_StoredAndValidated()
    {
        var explanation = (await Explain.ExecuteAsync(RepositoryId, HandlerId, regenerate: false, null, Token)).Value;

        Assert.False(explanation.FromCache);
        Assert.Equal("Shop.Application.Orders.PlaceOrderHandler", explanation.ElementName);
        Assert.All(explanation.Answer.Citations, c => Assert.True(c.Verified, c.Problem));
        Assert.Equal(HandlerId, explanation.Answer.Context[0].ElementId);

        var prompt = Assert.Single(_server.ChatRequests).RootElement.GetProperty("messages");
        Assert.Contains("Explain the class \"Shop.Application.Orders.PlaceOrderHandler\"",
            prompt[prompt.GetArrayLength() - 1].GetProperty("content").GetString(), StringComparison.Ordinal);

        var conversations = (await _app.Services.GetRequiredService<AiConversations>().ListAsync(RepositoryId, Token)).Value;
        Assert.Equal("Explain: Shop.Application.Orders.PlaceOrderHandler", Assert.Single(conversations).Title);
    }

    [Fact]
    public async Task CachedExplanation_IsReused_UntilRegeneratedOrReanalyzed()
    {
        var first = (await Explain.ExecuteAsync(RepositoryId, HandlerId, regenerate: false, null, Token)).Value;

        var cached = (await Explain.ExecuteAsync(RepositoryId, HandlerId, regenerate: false, null, Token)).Value;
        Assert.True(cached.FromCache);
        Assert.Equal((first.Answer.Id, first.Answer.Content), (cached.Answer.Id, cached.Answer.Content));
        Assert.Equal(first.Answer.Citations, cached.Answer.Citations);
        Assert.Equal(first.Answer.Context, cached.Answer.Context);
        Assert.Single(_server.ChatRequests);

        var regenerated = (await Explain.ExecuteAsync(RepositoryId, HandlerId, regenerate: true, null, Token)).Value;
        Assert.False(regenerated.FromCache);
        Assert.Equal(2, _server.ChatRequests.Count());

        _stored = await _repository.ImportAsync(_app.Services, Token); // a new run invalidates the cache
        var afterReanalysis = (await Explain.ExecuteAsync(RepositoryId, HandlerId, regenerate: false, null, Token)).Value;
        Assert.False(afterReanalysis.FromCache);
        Assert.Equal(3, _server.ChatRequests.Count());
        Assert.True((await Explain.ExecuteAsync(RepositoryId, HandlerId, regenerate: false, null, Token)).Value.FromCache);
    }

    [Fact]
    public async Task UnknownElement_AndDisabledAi_AreRefused()
    {
        Assert.Equal(InspectorErrorCodes.ElementNotFound,
            (await Explain.ExecuteAsync(RepositoryId, "0123456789abcdef0123456789abcdef", false, null, Token)).Error.Code);

        await _app.Services.GetRequiredService<AiSettingsStore>().SaveAsync(AiSettings.Default, Token);
        Assert.Equal(AiErrorCodes.Disabled, (await Explain.ExecuteAsync(RepositoryId, HandlerId, false, null, Token)).Error.Code);
        Assert.Empty(_server.ChatRequests);
    }

    [Fact]
    public async Task InspectorAction_ShowsTheExplanation_AndForwardsCitations()
    {
        var open = (await _app.Services.GetRequiredService<OpenRepository>().ExecuteAsync(RepositoryId, Token)).Value;
        _app.Services.GetRequiredService<RepositorySession>().SetCurrent(open);
        var explanation = _app.Services.GetRequiredService<ComponentExplanationViewModel>();
        AiCitationItem? requested = null;
        explanation.CitationRequested += (_, citation) => requested = citation;

        Assert.False(explanation.ExplainCommand.CanExecute(null));
        explanation.SetElement(HandlerId);
        await explanation.ExplainCommand.ExecuteAsync(null);

        Assert.NotNull(explanation.Explanation);
        Assert.False(explanation.Explanation.IsStreaming);
        Assert.NotEmpty(explanation.Explanation.Blocks);
        Assert.True(explanation.RegenerateCommand.CanExecute(null));

        explanation.OpenCitationCommand.Execute(explanation.Explanation.Citations[0]);
        Assert.Same(explanation.Explanation.Citations[0], requested);

        explanation.SetElement(null);
        Assert.Null(explanation.Explanation);
        Assert.False(explanation.ExplainCommand.CanExecute(null));
    }
}
