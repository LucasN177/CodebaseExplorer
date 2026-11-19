using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Common;
using Microsoft.Extensions.AI;

namespace CodebaseExplorer.AI.Providers;

/// <summary>
/// One AI provider of the registry (AI_ARCHITECTURE.md §2). Providers are only used behind the
/// <see cref="AiProviderGate"/>; they never decide themselves whether AI may run.
/// </summary>
internal interface IAiProvider
{
    /// <summary>Stable id stored in <c>ai.provider</c>.</summary>
    string Id { get; }

    /// <summary>Cloud providers always need consent (post-MVP); local providers only for non-loopback endpoints.</summary>
    bool IsCloud { get; }

    /// <summary>Health check and model listing; sends no repository content.</summary>
    Task<Result<AiProviderStatus>> CheckAsync(AiSettings settings, CancellationToken cancellationToken);

    /// <summary>Creates the chat client for the configured model. The caller disposes it.</summary>
    IChatClient CreateChatClient(AiSettings settings);
}
