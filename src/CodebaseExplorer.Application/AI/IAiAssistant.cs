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

    /// <summary>
    /// Answers a question about the repository from retrieved facts and snippets (AI-006): gate (enabled, model,
    /// consent) → search index (built on first use) → retrieval → prompt → streaming answer → citation validation.
    /// Text pieces are reported to <paramref name="onText"/> while they arrive. Cancellation throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<Result<AiAnswer>> AskAsync(AiQuestion question, IProgress<string>? onText, CancellationToken cancellationToken);
}

/// <summary>A question to <see cref="IAiAssistant.AskAsync"/>.</summary>
/// <param name="Settings">The AI settings to use (loaded by the caller).</param>
/// <param name="Repository">The repository's current model.</param>
/// <param name="Text">The question.</param>
/// <param name="ElementId">The element the question is about (<c>N</c> format), or null for the whole repository.</param>
/// <param name="History">Earlier messages of the conversation (user and assistant), oldest first.</param>
public sealed record AiQuestion(
    AiSettings Settings,
    Repositories.StoredRepositoryModel Repository,
    string Text,
    string? ElementId,
    IReadOnlyList<AiPromptMessage> History);

/// <summary>A complete answer with its validated citations and the context it was generated from.</summary>
public sealed record AiAnswer(
    string Text,
    IReadOnlyList<AiCitation> Citations,
    AiContextManifest Manifest,
    string ProviderId,
    string ModelId,
    bool IsRemote,
    int? PromptTokens,
    int? CompletionTokens);

/// <summary>Result of <see cref="IAiAssistant.CheckProviderAsync"/>.</summary>
/// <param name="ProviderId">The checked provider.</param>
/// <param name="Endpoint">The endpoint that answered.</param>
/// <param name="IsRemote">The endpoint is not on this machine.</param>
/// <param name="Models">Model names offered by the provider, sorted.</param>
/// <param name="Version">Provider version, if it reports one.</param>
public sealed record AiProviderStatus(string ProviderId, string Endpoint, bool IsRemote, IReadOnlyList<string> Models, string? Version);
