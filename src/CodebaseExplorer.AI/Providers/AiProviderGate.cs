using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.AI.Providers;

/// <summary>
/// The provider gate (AI_ARCHITECTURE.md §2): AI enabled, provider known and configured, and — for repository context
/// sent to a remote or cloud endpoint — the repository's consent. Nothing in the module talks to a provider without
/// passing here first.
/// </summary>
internal sealed class AiProviderGate(IEnumerable<IAiProvider> providers)
{
    // The last registration of an id wins, like any other DI registration (tests replace providers this way).
    private readonly IReadOnlyDictionary<string, IAiProvider> _providers =
        providers.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Known provider ids, sorted.</summary>
    public IReadOnlyList<string> ProviderIds => [.. _providers.Keys.Order(StringComparer.Ordinal)];

    /// <summary>For a connection check: enabled, known provider, valid endpoint. No repository content is involved.</summary>
    public Result<IAiProvider> ForConnection(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled)
        {
            return ApplicationError.Validation(AiErrorCodes.Disabled, "The AI assistant is switched off. Enable it in Settings.");
        }

        if (!_providers.TryGetValue(settings.ProviderId, out var provider))
        {
            return ApplicationError.Validation(AiErrorCodes.ProviderUnknown, $"The AI provider '{settings.ProviderId}' is not available.");
        }

        return settings.Validate() is { } error ? error : Result<IAiProvider>.Success(provider);
    }

    /// <summary>
    /// For a chat with context of <paramref name="repositoryId"/>: everything of <see cref="ForConnection"/>, a selected
    /// model, and consent if the endpoint is remote or the provider is a cloud provider.
    /// </summary>
    public Result<IAiProvider> ForChat(AiSettings settings, string repositoryId)
    {
        if (!ForConnection(settings).TryGetValue(out var provider, out var error))
        {
            return error;
        }

        if (string.IsNullOrWhiteSpace(settings.ModelId))
        {
            return ApplicationError.Validation(AiErrorCodes.ModelMissing, "Select a model for the AI assistant in Settings.");
        }

        if ((provider.IsCloud || settings.IsRemoteEndpoint) && !settings.HasRemoteConsent(repositoryId))
        {
            return ApplicationError.Validation(AiErrorCodes.ConsentRequired,
                $"{settings.Endpoint} is not on this machine. Context of this repository is sent there only after you allow it.",
                new Dictionary<string, string> { ["endpoint"] = settings.Endpoint, ["repositoryId"] = repositoryId });
        }

        return Result<IAiProvider>.Success(provider);
    }
}
