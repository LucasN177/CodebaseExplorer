using System.Diagnostics;
using System.Net;
using System.Text;
using CodebaseExplorer.AI.Citations;
using CodebaseExplorer.AI.Prompting;
using CodebaseExplorer.AI.Providers;
using CodebaseExplorer.AI.Retrieval;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Application.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.AI;

/// <summary>
/// <see cref="IAiAssistant"/> of the AI module. Every operation passes the <see cref="AiProviderGate"/> first; only then
/// is the search index built, context retrieved and a chat client created. The model gets no tools. Logs contain
/// sizes, durations and error kinds — never questions, context or answers (SECURITY.md).
/// </summary>
internal sealed class AiAssistant(
    AiProviderGate gate,
    EnsureSearchIndex searchIndex,
    ContextRetriever retriever,
    ILogger<AiAssistant> logger) : IAiAssistant
{
    /// <summary>Answers longer than this are cut (a looping model must not fill the database).</summary>
    public const int MaxAnswerLength = 40_000;

    public async Task<Result<AiProviderStatus>> CheckProviderAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        if (!gate.ForConnection(settings).TryGetValue(out var provider, out var error))
        {
            return error;
        }

        return await provider.CheckAsync(settings, cancellationToken);
    }

    public async Task<Result<AiAnswer>> AskAsync(AiQuestion question, IProgress<string>? onText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        var settings = question.Settings;
        var repositoryId = question.Repository.Repository.Id.ToString();
        if (!gate.ForChat(settings, repositoryId).TryGetValue(out var provider, out var gateError))
        {
            return gateError;
        }

        if (!(await searchIndex.ExecuteAsync(question.Repository, cancellationToken)).TryGetValue(out _, out var indexError))
        {
            return indexError;
        }

        var context = await retriever.RetrieveAsync(question.Repository, question.Text, question.ElementId, cancellationToken);
        var prompt = PromptBuilder.Build(question.Text, question.History, context, settings.MaxContextTokens);
        var messages = prompt.Messages.Select(m => new ChatMessage(Role(m.Role), m.Text)).ToList();
        var options = new ChatOptions
        {
            ModelId = settings.ModelId,
            Temperature = (float)settings.Temperature,
            MaxOutputTokens = Math.Max(256, settings.MaxContextTokens / 4),
            // No tools: the model can only produce text (ADR-008, AI_ARCHITECTURE.md §1.5).
        };

        var stopwatch = Stopwatch.StartNew();
        var text = new StringBuilder();
        var updates = new List<ChatResponseUpdate>();
        try
        {
            using var client = provider.CreateChatClient(settings);
            await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                updates.Add(update);
                var piece = update.Text;
                if (piece.Length == 0 || text.Length >= MaxAnswerLength)
                {
                    continue;
                }

                piece = piece.Length + text.Length > MaxAnswerLength ? piece[..(MaxAnswerLength - text.Length)] : piece;
                text.Append(piece);
                onText?.Report(piece);
            }

            // OllamaSharp ends the stream quietly when cancelled (AI-002); a cancelled answer is not an answer.
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (OllamaProvider.IsTransportFailure(exception, cancellationToken))
        {
            logger.LogWarning("The AI request to {ProviderId} failed after {ElapsedMs} ms: {Reason}", provider.Id, stopwatch.ElapsedMilliseconds,
                exception.GetType().Name);
            return exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
                ? ApplicationError.Failure(AiErrorCodes.ModelNotFound,
                    $"The model '{settings.ModelId}' is not available at {settings.Endpoint}. Download it (e.g. `ollama pull {settings.ModelId}`) or select another model in Settings.")
                : ApplicationError.Failure(AiErrorCodes.RequestFailed,
                    $"The AI provider at {settings.Endpoint} did not answer: {OllamaProvider.Describe(exception)}.");
        }

        var answer = text.ToString().Trim();
        if (answer.Length == 0)
        {
            return ApplicationError.Failure(AiErrorCodes.RequestFailed, "The model returned an empty answer. Try again or select another model.");
        }

        var usage = updates.ToChatResponse().Usage;
        var citations = CitationValidator.Validate(answer, prompt.Manifest, question.Repository.Model);
        logger.LogInformation(
            "AI answer from {ProviderId} in {ElapsedMs} ms: {AnswerLength} characters, {ContextEntries} context entries, {Citations} citations ({Unverified} unverified)",
            provider.Id, stopwatch.ElapsedMilliseconds, answer.Length, prompt.Manifest.Entries.Count, citations.Count, citations.Count(c => !c.Verified));
        return new AiAnswer(answer, citations, prompt.Manifest, provider.Id, settings.ModelId!, provider.IsCloud || settings.IsRemoteEndpoint,
            (int?)usage?.InputTokenCount, (int?)usage?.OutputTokenCount);
    }

    private static ChatRole Role(AiRole role) => role switch
    {
        AiRole.System => ChatRole.System,
        AiRole.Assistant => ChatRole.Assistant,
        _ => ChatRole.User,
    };
}
