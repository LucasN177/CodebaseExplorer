using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Inspector;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.AI;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Use case: explain the selected component with the AI assistant (AI-008). The explanation is generated from the
/// element's facts, relationships, declaration and evidence (scoped retrieval), validated like every answer, stored as a
/// conversation "Explain: …" and cached as an <see cref="AiArtifact"/> for the current analysis run. A cached explanation
/// is reused until the repository is analyzed again (or the user asks to regenerate it).
/// </summary>
public sealed class ExplainComponent(
    AiSettingsStore settingsStore,
    IArchitectureModelStore modelStore,
    IAiConversationStore conversations,
    IAiArtifactStore artifacts,
    IAiAssistant assistant,
    TimeProvider time)
{
    /// <param name="repositoryId">The repository (<c>N</c> format).</param>
    /// <param name="elementId">A project, module, type/member or external dependency of the current model (<c>N</c> format).</param>
    /// <param name="regenerate">Ignore a cached explanation and generate a new one.</param>
    /// <param name="onText">Receives answer text pieces while they stream (not called for a cached explanation).</param>
    /// <param name="cancellationToken">Cancels the request; nothing is stored then.</param>
    public async Task<Result<AiExplanationDto>> ExecuteAsync(
        string repositoryId,
        string elementId,
        bool regenerate,
        IProgress<string>? onText,
        CancellationToken cancellationToken)
    {
        if (!RepositoryContractMapping.TryParseRepositoryId(repositoryId, out var id))
        {
            return ApplicationError.Validation(RepositoryErrorCodes.IdInvalid, "The repository id is not a valid id.");
        }

        if (elementId is not { Length: 32 } || !Guid.TryParseExact(elementId, "N", out var elementGuid))
        {
            return ApplicationError.Validation(InspectorErrorCodes.ElementInvalid, "The element id is not a valid id.");
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

        var element = new ElementId(elementGuid);
        if (Describe(stored.Model, element) is not { } described)
        {
            return ApplicationError.NotFound(InspectorErrorCodes.ElementNotFound, "The element is not part of the current model.");
        }

        if (!regenerate
            && await artifacts.FindAsync(id, element, AiArtifact.ComponentExplanation, stored.Model.AnalysisRunId, cancellationToken) is { MessageId: { } messageId }
            && await conversations.FindMessageAsync(messageId, cancellationToken) is { } cached)
        {
            return new AiExplanationDto(elementId, described.Name, AiContractMapping.ToDto(cached.Message), cached.ConversationId.ToString("N"), FromCache: true);
        }

        var question = $"Explain the {described.KindText} \"{described.Name}\": what it is responsible for, how it is used by other parts " +
            "of the repository, and what it depends on. Cite the facts and snippets you use.";
        var result = await assistant.AskAsync(new AiQuestion(settings, stored, question, elementId, []), onText, cancellationToken);
        if (!result.TryGetValue(out var answer, out var error))
        {
            return error;
        }

        var now = time.GetUtcNow();
        var conversation = new AiConversation(Guid.CreateVersion7(now), id, $"Explain: {described.Name}", answer.ProviderId, answer.ModelId,
            answer.IsRemote, now, now);
        var userMessage = new AiMessage(Guid.CreateVersion7(now), 0, AiRole.User, question, [], null, null, null, now);
        var assistantMessage = new AiMessage(Guid.CreateVersion7(now), 1, AiRole.Assistant, answer.Text, answer.Citations, answer.Manifest,
            answer.PromptTokens, answer.CompletionTokens, now);
        await conversations.AppendAsync(conversation, [userMessage, assistantMessage], CancellationToken.None);
        await artifacts.SaveAsync(new AiArtifact(Guid.CreateVersion7(now), id, assistantMessage.Id, described.Kind, element,
            AiArtifact.ComponentExplanation, answer.Text, stored.Model.AnalysisRunId, now), CancellationToken.None);

        return new AiExplanationDto(elementId, described.Name, AiContractMapping.ToDto(assistantMessage), conversation.Id.ToString("N"), FromCache: false);
    }

    private static (ElementKind Kind, string Name, string KindText)? Describe(ArchitectureModel model, ElementId id)
    {
        if (model.FindProject(id) is { } project)
        {
            return (ElementKind.Project, project.Name, "project");
        }

        if (model.FindModule(id) is { } module)
        {
            return (ElementKind.Module, module.QualifiedName, GetElementDetails.ModuleKindText(module.Kind).ToLowerInvariant());
        }

        if (model.FindEntity(id) is { } entity)
        {
            return (ElementKind.Entity, entity.QualifiedName, GetElementDetails.KindText(entity.Kind).ToLowerInvariant());
        }

        return model.FindExternalDependency(id) is { } dependency
            ? (ElementKind.ExternalDependency, dependency.DisplayName, "external dependency")
            : null;
    }
}
