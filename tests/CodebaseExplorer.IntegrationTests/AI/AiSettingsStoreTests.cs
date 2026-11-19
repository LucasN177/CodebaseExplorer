using CodebaseExplorer.Application.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>AI-001: AI settings are stored as user settings; consent is bound to the endpoint.</summary>
public sealed class AiSettingsStoreTests : IAsyncLifetime
{
    private const string Repository = "0123456789abcdef0123456789abcdef";
    private readonly TestApplication _app = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private AiSettingsStore Store => _app.Services.GetRequiredService<AiSettingsStore>();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task NothingStored_LoadsTheDefaults()
    {
        Assert.Equal(AiSettings.Default, await Store.LoadAsync(Token) with { RemoteConsents = AiSettings.Default.RemoteConsents });
        Assert.Empty((await Store.LoadAsync(Token)).RemoteConsents);
    }

    [Fact]
    public async Task SavedSettings_SurviveARestart()
    {
        var saved = (await Store.SaveAsync(AiSettings.Default with
        {
            Enabled = true,
            Endpoint = "http://127.0.0.1:11434/",
            ModelId = " llama3.2:3b ",
            MaxContextTokens = 32768,
            Temperature = 0.7,
        }, Token)).Value;

        await using var restarted = _app.Restart();
        var loaded = await restarted.Services.GetRequiredService<AiSettingsStore>().LoadAsync(Token);

        Assert.True(loaded.Enabled);
        Assert.Equal("http://127.0.0.1:11434", loaded.Endpoint);
        Assert.Equal("llama3.2:3b", loaded.ModelId);
        Assert.Equal(32768, loaded.MaxContextTokens);
        Assert.Equal(0.7, loaded.Temperature);
        Assert.Equal(saved.Endpoint, loaded.Endpoint);
    }

    [Fact]
    public async Task Consent_IsKept_UntilTheEndpointChanges()
    {
        var remote = (await Store.SaveAsync(AiSettings.Default with { Enabled = true, Endpoint = "http://gpu-box:11434" }, Token)).Value;
        await Store.GrantRemoteConsentAsync(Repository, Token);
        Assert.True((await Store.LoadAsync(Token)).HasRemoteConsent(Repository));

        var sameEndpoint = (await Store.SaveAsync((await Store.LoadAsync(Token)) with { ModelId = "m" }, Token)).Value;
        Assert.True(sameEndpoint.HasRemoteConsent(Repository));

        var moved = (await Store.SaveAsync(sameEndpoint with { Endpoint = "http://other-box:11434" }, Token)).Value;
        Assert.False(moved.HasRemoteConsent(Repository));
        Assert.False((await Store.LoadAsync(Token)).HasRemoteConsent(Repository));
        Assert.True(remote.IsRemoteEndpoint);
    }

    [Fact]
    public async Task InvalidSettings_AreNotStored()
    {
        var result = await Store.SaveAsync(AiSettings.Default with { Enabled = true, Endpoint = "not a url" }, Token);

        Assert.Equal(AiErrorCodes.EndpointInvalid, result.Error.Code);
        Assert.False((await Store.LoadAsync(Token)).Enabled);
    }
}
