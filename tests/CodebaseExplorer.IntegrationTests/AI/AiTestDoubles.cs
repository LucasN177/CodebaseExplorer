using CodebaseExplorer.AI.Providers;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Common;
using Microsoft.Extensions.AI;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>An HTTP handler that fails the test if anything tries to send a request (AI-001 network guard).</summary>
internal sealed class GuardedHttpHandler : HttpMessageHandler
{
    private int _requests;
    private int _created;

    public int Requests => _requests;

    /// <summary>How many handlers the transport asked for (a client is created per call).</summary>
    public int Created => _created;

    public Func<HttpMessageHandler> Factory => () =>
    {
        Interlocked.Increment(ref _created);
        return new Forwarder(this);
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        throw new InvalidOperationException($"Unexpected outbound request to {request.RequestUri}.");
    }

    private sealed class Forwarder(GuardedHttpHandler owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref owner._requests);
            throw new InvalidOperationException($"Unexpected outbound request to {request.RequestUri}.");
        }
    }
}

/// <summary>A provider that records whether a chat client was ever constructed.</summary>
internal sealed class SpyProvider(string id = AiSettings.OllamaProviderId) : IAiProvider
{
    public int ChatClientsCreated { get; private set; }

    public int Checks { get; private set; }

    public string Id => id;

    public bool IsCloud => false;

    public Func<IChatClient>? ChatClient { get; set; }

    public Task<Result<AiProviderStatus>> CheckAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        Checks++;
        return Task.FromResult<Result<AiProviderStatus>>(new AiProviderStatus(Id, settings.Endpoint, settings.IsRemoteEndpoint, ["spy-model"], null));
    }

    public void ConfigureOptions(ChatOptions options, AiSettings settings)
    {
    }

    public IChatClient CreateChatClient(AiSettings settings)
    {
        ChatClientsCreated++;
        return ChatClient?.Invoke() ?? throw new InvalidOperationException("No chat client configured for the spy.");
    }
}
