using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.AI;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Use case: ask the AI assistant about the analyzed repository (AI-006, USER_WORKFLOWS.md). Loads the AI settings and
/// the current model, continues or starts a conversation, lets <see cref="IAiAssistant"/> answer from retrieved context,
/// and stores question and answer (with citations and context manifest) only when an answer was produced. A failed or
/// cancelled question leaves no trace in the conversation.
/// </summary>
public sealed class AskRepositoryQuestion(
    AiSettingsStore settingsStore,
    IArchitectureModelStore modelStore,
    IAiConversationStore conversations,
    IAiAssistant assistant,
    TimeProvider time)
{
    public const int MaxQuestionLength = 4000;

    public const int MaxTitleLength = 80;

    /// <param name="repositoryId">The repository (<c>N</c> format).</param>
    /// <param name="conversationId">The conversation to continue, or null to start a new one.</param>
    /// <param name="question">The question.</param>
    /// <param name="elementId">The element the question is about, or null for the whole repository.</param>
    /// <param name="onText">Receives answer text pieces while they stream.</param>
    /// <param name="cancellationToken">Cancels the request; nothing is stored then.</param>
    public async Task<Result<AiExchangeDto>> ExecuteAsync(
        string repositoryId,
        string? conversationId,
        string question,
        string? elementId,
        IProgress<string>? onText,
        CancellationToken cancellationToken)
    {
        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        var text = question?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxQuestionLength)
        {
            return ApplicationError.Validation(AiErrorCodes.QuestionInvalid, $"Enter a question of at most {MaxQuestionLength} characters.");
        }

        var settings = await settingsStore.LoadAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return ApplicationError.Validation(AiErrorCodes.Disabled, "The AI assistant is switched off. Enable it in Settings.");
        }

        if (await modelStore.LoadCurrentModelAsync(id, cancellationToken) is not { } stored)
        {
            return ApplicationError.NotFound(RepositoryErrorCodes.NotAnalyzed, "The repository has not been analyzed yet.");
        }

        AiConversation? existing = null;
        IReadOnlyList<AiMessage> earlier = [];
        if (conversationId is not null)
        {
            if (!Guid.TryParseExact(conversationId, "N", out var conversationGuid)
                || await conversations.FindAsync(conversationGuid, cancellationToken) is not { } found
                || found.RepositoryId != id)
            {
                return ApplicationError.NotFound(AiErrorCodes.ConversationNotFound, "The conversation does not exist any more.");
            }

            existing = found;
            earlier = await conversations.LoadMessagesAsync(found.Id, cancellationToken);
        }

        var history = earlier.Where(m => m.Role is AiRole.User or AiRole.Assistant).Select(m => new AiPromptMessage(m.Role, m.Content)).ToList();
        var result = await assistant.AskAsync(new AiQuestion(settings, stored, text, elementId, history), onText, cancellationToken);
        if (!result.TryGetValue(out var answer, out var error))
        {
            return error;
        }

        var now = time.GetUtcNow();
        var conversation = (existing ?? new AiConversation(Guid.CreateVersion7(now), id, Title(text), answer.ProviderId, answer.ModelId,
            answer.IsRemote, now, now)) with
        {
            ModelId = answer.ModelId,
            IsRemote = (existing?.IsRemote ?? false) || answer.IsRemote,
            UpdatedAt = now,
        };
        var ordinal = earlier.Count == 0 ? 0 : earlier.Max(m => m.Ordinal) + 1;
        var userMessage = new AiMessage(Guid.CreateVersion7(now), ordinal, AiRole.User, text, [], null, null, null, now);
        var assistantMessage = new AiMessage(Guid.CreateVersion7(now), ordinal + 1, AiRole.Assistant, answer.Text, answer.Citations, answer.Manifest,
            answer.PromptTokens, answer.CompletionTokens, now);
        await conversations.AppendAsync(conversation, [userMessage, assistantMessage], CancellationToken.None);

        return new AiExchangeDto(AiContractMapping.ToDto(conversation), AiContractMapping.ToDto(userMessage), AiContractMapping.ToDto(assistantMessage));
    }

    private static string Title(string question)
    {
        var line = question.ReplaceLineEndings(" ");
        return line.Length <= MaxTitleLength ? line : string.Concat(line.AsSpan(0, MaxTitleLength - 1), "…");
    }
}
