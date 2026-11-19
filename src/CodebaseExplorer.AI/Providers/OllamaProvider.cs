using System.Text.Json;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace CodebaseExplorer.AI.Providers;

/// <summary>
/// Ollama through OllamaSharp's <see cref="IChatClient"/> (AI-002, ADR-008). The endpoint defaults to
/// <c>http://localhost:11434</c>; a non-loopback endpoint is treated as remote by the gate. All HTTP goes through
/// <see cref="AiTransport"/>.
/// </summary>
internal sealed class OllamaProvider(AiTransport transport, ILogger<OllamaProvider> logger) : IAiProvider
{
    /// <summary>Health check and model listing must answer quickly; a missing server should not block the UI.</summary>
    internal static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Upper bound for one answer; local models on a CPU can be slow, but a request must end eventually.</summary>
    internal static readonly TimeSpan ChatTimeout = TimeSpan.FromMinutes(10);

    public string Id => AiSettings.OllamaProviderId;

    public bool IsCloud => false;

    public async Task<Result<AiProviderStatus>> CheckAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        var endpoint = settings.EndpointUri ?? throw new ArgumentException("The endpoint was not validated.", nameof(settings));
        using var http = transport.CreateClient(endpoint, CheckTimeout);
        using var client = new OllamaApiClient(http);
        try
        {
            if (!await client.IsRunningAsync(cancellationToken))
            {
                return Unavailable(settings, "it did not answer like an Ollama server");
            }

            var models = (await client.ListLocalModelsAsync(cancellationToken))
                .Select(m => m.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Order(StringComparer.Ordinal)
                .ToList();
            string? version = null;
            try
            {
                version = (await client.GetVersionAsync(cancellationToken))?.ToString();
            }
            catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
            {
                // Older servers have no /api/version; the version is informational only.
                logger.LogDebug(exception, "Ollama at {Endpoint} reported no version", settings.Endpoint);
            }

            logger.LogInformation("Ollama at {Endpoint} answered with {ModelCount} model(s)", settings.Endpoint, models.Count);
            return new AiProviderStatus(Id, settings.Endpoint, settings.IsRemoteEndpoint, models, version);
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            logger.LogWarning("Ollama at {Endpoint} could not be reached: {Reason}", settings.Endpoint, exception.GetType().Name);
            return Unavailable(settings, Describe(exception));
        }
    }

    public IChatClient CreateChatClient(AiSettings settings)
    {
        var endpoint = settings.EndpointUri ?? throw new ArgumentException("The endpoint was not validated.", nameof(settings));
        var http = transport.CreateClient(endpoint, ChatTimeout);
        return new OwningChatClient(new OllamaApiClient(http, settings.ModelId ?? string.Empty), http);
    }

    /// <summary>
    /// Ollama's default context window (<c>num_ctx</c>, 2048 or 4096 depending on the version) is smaller than the budget
    /// of the prompt; without this Ollama silently drops the beginning of the prompt (system rules and facts).
    /// </summary>
    public void ConfigureOptions(ChatOptions options, AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddOllamaOption(OllamaSharp.Models.OllamaOption.NumCtx, settings.MaxContextTokens);
    }

    /// <summary>Network, protocol and timeout failures (expected when the server is down); not the caller's cancellation.</summary>
    internal static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or InvalidOperationException or NotSupportedException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    internal static string Describe(Exception exception) => exception switch
    {
        OperationCanceledException => "it did not answer in time",
        HttpRequestException { StatusCode: { } status } => $"it answered with HTTP {(int)status}",
        HttpRequestException => "the connection failed (is Ollama running?)",
        _ => "its answer could not be read",
    };

    private static ApplicationError Unavailable(AiSettings settings, string reason) =>
        ApplicationError.Failure(AiErrorCodes.ProviderUnavailable, $"Ollama at {settings.Endpoint} is not available: {reason}.",
            new Dictionary<string, string> { ["endpoint"] = settings.Endpoint });

    /// <summary>Disposes the HTTP client together with the chat client (OllamaSharp keeps external clients alive).</summary>
    private sealed class OwningChatClient(IChatClient inner, HttpClient http) : DelegatingChatClient(inner)
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                http.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
