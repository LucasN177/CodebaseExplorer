using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// The optional AI assistant (port; implemented by the AI module, AI_ARCHITECTURE.md §2). Every call passes the provider
/// gate first: with AI disabled (the default) nothing is constructed and nothing is sent (<see cref="AiErrorCodes.Disabled"/>).
/// </summary>
public interface IAiAssistant
{
    /// <summary>
    /// Checks that the provider configured in <paramref name="settings"/> is reachable and lists its models. Sends no
    /// repository content.
    /// </summary>
    Task<Result<AiProviderStatus>> CheckProviderAsync(AiSettings settings, CancellationToken cancellationToken);
}

/// <summary>Result of <see cref="IAiAssistant.CheckProviderAsync"/>.</summary>
/// <param name="ProviderId">The checked provider.</param>
/// <param name="Endpoint">The endpoint that answered.</param>
/// <param name="IsRemote">The endpoint is not on this machine.</param>
/// <param name="Models">Model names offered by the provider, sorted.</param>
/// <param name="Version">Provider version, if it reports one.</param>
public sealed record AiProviderStatus(string ProviderId, string Endpoint, bool IsRemote, IReadOnlyList<string> Models, string? Version);
