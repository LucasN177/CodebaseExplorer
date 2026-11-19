using System.Globalization;
using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Configuration of the optional AI assistant (AI_ARCHITECTURE.md §3), stored in <c>UserSettings</c>
/// (<see cref="AiSettingsStore"/>). AI is disabled by default; secrets are never part of these settings (OPD-03).
/// </summary>
public sealed record AiSettings
{
    /// <summary>The only provider of the MVP: Ollama (ADR-008).</summary>
    public const string OllamaProviderId = "ollama";

    public const string DefaultEndpoint = "http://localhost:11434";

    public const int DefaultMaxContextTokens = 8192;

    public const int MinContextTokens = 2048;

    public const int MaxContextTokensLimit = 262_144;

    public const double DefaultTemperature = 0.2;

    /// <summary>The defaults: AI off, Ollama on this machine, no model selected.</summary>
    public static AiSettings Default { get; } = new();

    /// <summary>AI features are available only when this is true (the default is false).</summary>
    public bool Enabled { get; init; }

    public string ProviderId { get; init; } = OllamaProviderId;

    /// <summary>Base URL of the provider, e.g. <c>http://localhost:11434</c>.</summary>
    public string Endpoint { get; init; } = DefaultEndpoint;

    /// <summary>Model name at the provider, e.g. <c>qwen2.5-coder:7b</c>; null until the user picks one.</summary>
    public string? ModelId { get; init; }

    /// <summary>Context window the prompt is budgeted for (prompt and answer together).</summary>
    public int MaxContextTokens { get; init; } = DefaultMaxContextTokens;

    public double Temperature { get; init; } = DefaultTemperature;

    /// <summary>
    /// Repositories (ids, <c>N</c> format) whose context may be sent to a remote endpoint. Cleared whenever the endpoint
    /// changes, so a consent always refers to the endpoint it was given for (AI_ARCHITECTURE.md §5).
    /// </summary>
    public IReadOnlyList<string> RemoteConsents { get; init; } = [];

    /// <summary>The endpoint as URL, if it is an absolute http(s) URL without user information.</summary>
    public Uri? EndpointUri =>
        Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.UserInfo.Length == 0
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
            ? uri
            : null;

    /// <summary>
    /// True unless the endpoint is on this machine (loopback). Remote endpoints are treated like cloud providers and
    /// need per-repository consent before repository context is sent (SECURITY.md "AI").
    /// </summary>
    public bool IsRemoteEndpoint => EndpointUri is not { IsLoopback: true };

    public bool HasRemoteConsent(string repositoryId) =>
        RemoteConsents.Contains(repositoryId, StringComparer.OrdinalIgnoreCase);

    /// <summary>Checks the values that can be wrong independently of the provider; null if they are fine.</summary>
    public ApplicationError? Validate()
    {
        if (string.IsNullOrWhiteSpace(ProviderId))
        {
            return ApplicationError.Validation(AiErrorCodes.ProviderUnknown, "Select an AI provider.");
        }

        if (EndpointUri is null)
        {
            return ApplicationError.Validation(AiErrorCodes.EndpointInvalid,
                "The endpoint must be an http or https URL without user name, password, query or fragment, e.g. " + DefaultEndpoint + ".");
        }

        if (MaxContextTokens is < MinContextTokens or > MaxContextTokensLimit)
        {
            return ApplicationError.Validation(AiErrorCodes.SettingInvalid, string.Create(CultureInfo.InvariantCulture,
                $"The context size must be between {MinContextTokens} and {MaxContextTokensLimit} tokens."));
        }

        if (double.IsNaN(Temperature) || Temperature is < 0 or > 2)
        {
            return ApplicationError.Validation(AiErrorCodes.SettingInvalid, "The temperature must be between 0 and 2.");
        }

        return null;
    }
}
