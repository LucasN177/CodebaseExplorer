using CodebaseExplorer.AI.Providers;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>AI-001: the provider gate refuses unless AI is enabled, configured and — for remote endpoints — consented.</summary>
public sealed class AiProviderGateTests
{
    private const string Repository = "0123456789abcdef0123456789abcdef";
    private readonly AiProviderGate _gate = new([new SpyProvider()]);

    private static AiSettings Enabled => AiSettings.Default with { Enabled = true, ModelId = "model" };

    [Fact]
    public void DefaultSettings_AreRefused()
    {
        Assert.Equal(AiErrorCodes.Disabled, _gate.ForConnection(AiSettings.Default).Error.Code);
        Assert.Equal(AiErrorCodes.Disabled, _gate.ForChat(AiSettings.Default, Repository).Error.Code);
    }

    [Fact]
    public void LocalEndpointWithModel_IsAllowed()
    {
        Assert.True(_gate.ForChat(Enabled, Repository).IsSuccess);
        Assert.True(_gate.ForChat(Enabled with { Endpoint = "http://127.0.0.1:11434" }, Repository).IsSuccess);
        Assert.True(_gate.ForChat(Enabled with { Endpoint = "http://[::1]:11434" }, Repository).IsSuccess);
    }

    [Theory]
    [InlineData("other", AiErrorCodes.ProviderUnknown)]
    [InlineData("ollama", AiErrorCodes.ModelMissing)]
    public void UnknownProviderOrMissingModel_IsRefused(string provider, string code) =>
        Assert.Equal(code, _gate.ForChat(Enabled with { ProviderId = provider, ModelId = null }, Repository).Error.Code);

    [Theory]
    [InlineData("localhost:11434")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://user:secret@localhost:11434")]
    [InlineData("http://localhost:11434/?redirect=x")]
    [InlineData("")]
    public void InvalidEndpoints_AreRefused(string endpoint) =>
        Assert.Equal(AiErrorCodes.EndpointInvalid, _gate.ForConnection(Enabled with { Endpoint = endpoint }).Error.Code);

    [Fact]
    public void RemoteEndpoint_NeedsConsentOfTheRepository()
    {
        var remote = Enabled with { Endpoint = "http://gpu-box.internal:11434" };

        Assert.True(_gate.ForConnection(remote).IsSuccess);
        var refused = _gate.ForChat(remote, Repository);
        Assert.Equal(AiErrorCodes.ConsentRequired, refused.Error.Code);
        Assert.Equal(ErrorKind.Validation, refused.Error.Kind);

        Assert.True(_gate.ForChat(remote with { RemoteConsents = [Repository] }, Repository).IsSuccess);
        Assert.Equal(AiErrorCodes.ConsentRequired,
            _gate.ForChat(remote with { RemoteConsents = [Repository] }, "fedcba9876543210fedcba9876543210").Error.Code);
    }

    [Theory]
    [InlineData(1000, 0.2)]
    [InlineData(8192, -0.1)]
    [InlineData(8192, 2.5)]
    public void OutOfRangeValues_AreRefused(int tokens, double temperature) =>
        Assert.Equal(AiErrorCodes.SettingInvalid,
            _gate.ForConnection(Enabled with { MaxContextTokens = tokens, Temperature = temperature }).Error.Code);
}
