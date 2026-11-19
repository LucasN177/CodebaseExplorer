namespace CodebaseExplorer.Contracts.AI;

/// <summary>
/// An AI explanation of a component (AI-008). <c>FromCache</c> is true when it was generated earlier for the same
/// analysis run; <c>ConversationId</c> is the conversation that holds it (shown in the assistant's history).
/// </summary>
public sealed record AiExplanationDto(string ElementId, string ElementName, AiMessageDto Answer, string ConversationId, bool FromCache);
