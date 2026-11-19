using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.AI;

namespace CodebaseExplorer.Application.AI;

/// <summary>Use cases of the conversation history (AI-006): list a repository's conversations, read one, delete one.</summary>
public sealed class AiConversations(IAiConversationStore store)
{
    public async Task<Result<IReadOnlyList<AiConversationDto>>> ListAsync(string repositoryId, CancellationToken cancellationToken)
    {
        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        var list = await store.ListAsync(id, cancellationToken);
        return Result<IReadOnlyList<AiConversationDto>>.Success([.. list.Select(AiContractMapping.ToDto)]);
    }

    public async Task<Result<IReadOnlyList<AiMessageDto>>> GetMessagesAsync(string conversationId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(conversationId, "N", out var id) || await store.FindAsync(id, cancellationToken) is null)
        {
            return ApplicationError.NotFound(AiErrorCodes.ConversationNotFound, "The conversation does not exist any more.");
        }

        var messages = await store.LoadMessagesAsync(id, cancellationToken);
        return Result<IReadOnlyList<AiMessageDto>>.Success([.. messages.Select(AiContractMapping.ToDto)]);
    }

    public async Task<Result> DeleteAsync(string conversationId, CancellationToken cancellationToken) =>
        Guid.TryParseExact(conversationId, "N", out var id) && await store.DeleteAsync(id, cancellationToken)
            ? Result.Success()
            : Result.Failure(ApplicationError.NotFound(AiErrorCodes.ConversationNotFound, "The conversation does not exist any more."));
}
