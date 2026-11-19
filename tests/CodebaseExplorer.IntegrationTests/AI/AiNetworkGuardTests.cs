using CodebaseExplorer.AI.Providers;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Settings;
using CodebaseExplorer.Desktop.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>
/// AI-001 acceptance (SECURITY.md §9 "No network with AI disabled"): with default settings no chat client is
/// constructed and no HTTP handler is invoked — through the real composition root and the real settings store.
/// </summary>
public sealed class AiNetworkGuardTests : IAsyncLifetime
{
    private readonly GuardedHttpHandler _network = new();
    private readonly SpyProvider _provider = new();
    private readonly TestApplication _app;

    public AiNetworkGuardTests()
    {
        _app = new TestApplication(services =>
        {
            services.AddSingleton(new AiTransport(_network.Factory));
            services.AddSingleton<IAiProvider>(_provider);
        });
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task DefaultSettings_ConstructNoChatClient_AndSendNothing()
    {
        var settings = await _app.Services.GetRequiredService<AiSettingsStore>().LoadAsync(Token);
        var assistant = _app.Services.GetRequiredService<IAiAssistant>();

        var check = await assistant.CheckProviderAsync(settings, Token);

        Assert.False(settings.Enabled);
        Assert.Equal(AiErrorCodes.Disabled, check.Error.Code);
        Assert.Equal(0, _provider.Checks);
        Assert.Equal(0, _provider.ChatClientsCreated);
        Assert.Equal(0, _network.Created);
        Assert.Equal(0, _network.Requests);
    }

    [Fact]
    public async Task AskingWithDefaultSettings_IndexesNothing_AndSendsNothing()
    {
        using var repository = new NamedFixtureCopy("dotnet-layered");
        var stored = await repository.ImportAsync(_app.Services, Token);

        var result = await _app.Services.GetRequiredService<AskRepositoryQuestion>().ExecuteAsync(
            stored.Repository.Id.ToString(), null, "How are orders placed?", null, null, Token);

        Assert.Equal(AiErrorCodes.Disabled, result.Error.Code);
        Assert.Null(await _app.Services.GetRequiredService<ISearchIndexStore>().GetStateAsync(stored.Repository.Id, Token));
        Assert.Equal(0, _provider.ChatClientsCreated);
        Assert.Equal(0, _network.Created);
        Assert.Equal(0, _network.Requests);
    }

    [Fact]
    public async Task DamagedEnabledFlag_KeepsAiOff()
    {
        await _app.Services.GetRequiredService<IUserSettingsStore>().SetAsync(UserSettingKeys.AiEnabled, "yes", Token);

        var settings = await _app.Services.GetRequiredService<AiSettingsStore>().LoadAsync(Token);

        Assert.False(settings.Enabled);
    }

    [Fact]
    public async Task DefaultSettings_WithTheRealOllamaProvider_SendNothing()
    {
        var network = new GuardedHttpHandler();
        await using var app = new TestApplication(services => services.AddSingleton(new AiTransport(network.Factory)));
        await app.InitializeAsync();

        var settings = await app.Services.GetRequiredService<AiSettingsStore>().LoadAsync(Token);
        var check = await app.Services.GetRequiredService<IAiAssistant>().CheckProviderAsync(settings, Token);

        Assert.Equal(AiErrorCodes.Disabled, check.Error.Code);
        Assert.Equal(0, network.Created);
        Assert.Equal(0, network.Requests);
    }

    [Fact]
    public async Task EnabledSettings_CheckTheConfiguredOllama()
    {
        await using var server = new StubOllamaServer();
        await using var app = new TestApplication(overrides: null);
        await app.InitializeAsync();

        var check = await app.Services.GetRequiredService<IAiAssistant>().CheckProviderAsync(
            AiSettings.Default with { Enabled = true, Endpoint = server.Endpoint }, Token);

        Assert.Equal(["llama3.2:3b", "qwen2.5-coder:7b"], check.Value.Models);
    }

    [Fact]
    public void BuildingTheApplication_ConstructsNoChatClient()
    {
        _ = _app.Services.GetRequiredService<IAiAssistant>();

        Assert.Equal(0, _provider.ChatClientsCreated);
        Assert.Equal(0, _network.Created);
    }
}
