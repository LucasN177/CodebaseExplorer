using CodebaseExplorer.Contracts.AI;

namespace CodebaseExplorer.Application.AI;

/// <summary>Maps AI records of the Application layer to the Contracts DTOs used by ViewModels.</summary>
internal static class AiContractMapping
{
    public static AiConversationDto ToDto(AiConversation conversation) =>
        new(conversation.Id.ToString("N"), conversation.Title, conversation.ProviderId, conversation.ModelId, conversation.IsRemote,
            conversation.CreatedAt, conversation.UpdatedAt);

    public static AiMessageDto ToDto(AiMessage message) =>
        new(message.Id.ToString("N"),
            message.Ordinal,
            message.Role == AiRole.Assistant ? AiMessageDto.AssistantRole : AiMessageDto.UserRole,
            message.Content,
            [.. message.Citations.Select(c => new AiCitationDto(c.Text, c.Key, c.Path, c.StartLine, c.EndLine, c.ElementId, c.Verified, c.Problem))],
            [.. (message.Manifest?.Entries ?? []).Select(e => new AiContextEntryDto(e.Key, e.Kind, e.Label, e.Path, e.StartLine, e.EndLine, e.ElementId))],
            message.PromptTokens,
            message.CompletionTokens,
            message.CreatedAt);
}
