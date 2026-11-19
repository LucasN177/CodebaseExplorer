using System.Text.Json;
using CodebaseExplorer.Application.AI;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Storage.Persistence;
using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Stores;

/// <summary>
/// SQLite implementation of <see cref="IAiConversationStore"/> (AI-006, DATABASE_SCHEMA.md §4.14–4.15). Citations and
/// the context manifest are JSON columns; conversations cascade with their repository.
/// </summary>
internal sealed class AiConversationStore(IDbContextFactory<CodebaseExplorerDbContext> contextFactory) : IAiConversationStore
{
    public async Task<IReadOnlyList<AiConversation>> ListAsync(RepositoryId repositoryId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var records = await context.AiConversations.AsNoTracking()
            .Where(c => c.RepositoryId == repositoryId.Value)
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);
        return [.. records.Select(ToDomain)];
    }

    public async Task<AiConversation?> FindAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.AiConversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        return record is null ? null : ToDomain(record);
    }

    public async Task<IReadOnlyList<AiMessage>> LoadMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var records = await context.AiMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Ordinal)
            .ToListAsync(cancellationToken);
        return [.. records.Select(ToDomain)];
    }

    public async Task<(Guid ConversationId, AiMessage Message)?> FindMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await context.AiMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        return record is null ? null : (record.ConversationId, ToDomain(record));
    }

    public async Task AppendAsync(AiConversation conversation, IReadOnlyList<AiMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(messages);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var record = await context.AiConversations.SingleOrDefaultAsync(c => c.Id == conversation.Id, cancellationToken);
        if (record is null)
        {
            context.AiConversations.Add(new AiConversationRecord
            {
                Id = conversation.Id,
                RepositoryId = conversation.RepositoryId.Value,
                Title = conversation.Title,
                ProviderId = conversation.ProviderId,
                ModelId = conversation.ModelId,
                IsCloudProvider = conversation.IsRemote,
                CreatedAt = conversation.CreatedAt,
                UpdatedAt = conversation.UpdatedAt,
            });
        }
        else
        {
            record.ModelId = conversation.ModelId;
            record.IsCloudProvider |= conversation.IsRemote;
            record.UpdatedAt = conversation.UpdatedAt;
        }

        var next = await context.AiMessages.Where(m => m.ConversationId == conversation.Id)
            .Select(m => (int?)m.Ordinal).MaxAsync(cancellationToken) is { } last ? last + 1 : 0;
        foreach (var message in messages)
        {
            context.AiMessages.Add(ToRecord(conversation.Id, next++, message));
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.AiConversations.Where(c => c.Id == conversationId).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    private static AiConversation ToDomain(AiConversationRecord record) =>
        new(record.Id, new RepositoryId(record.RepositoryId), record.Title, record.ProviderId, record.ModelId, record.IsCloudProvider,
            record.CreatedAt, record.UpdatedAt);

    private static AiMessage ToDomain(AiMessageRecord record)
    {
        var citations = record.CitationsJson is { } citationsJson
            ? JsonSerializer.Deserialize(citationsJson, StorageJsonContext.Default.ListAiCitationJson) ?? []
            : [];
        var manifest = record.ContextManifestJson is { } manifestJson
            ? JsonSerializer.Deserialize(manifestJson, StorageJsonContext.Default.AiManifestJson)
            : null;
        return new AiMessage(
            record.Id,
            record.Ordinal,
            Enum.TryParse<AiRole>(record.Role, out var role) ? role : AiRole.User,
            record.Content,
            [.. citations.Select(c => new AiCitation(c.Text, c.Key, c.Path, c.StartLine, c.EndLine, c.ElementId, c.Verified, c.Problem))],
            manifest is null
                ? null
                : new AiContextManifest(
                    [.. manifest.Entries.Select(e => new AiContextEntry(e.Key, e.Kind, e.Label, e.Path, e.StartLine, e.EndLine, e.ElementId, e.Tokens))],
                    manifest.BudgetTokens, manifest.UsedTokens, manifest.OmittedEntries),
            record.PromptTokens,
            record.CompletionTokens,
            record.CreatedAt);
    }

    private static AiMessageRecord ToRecord(Guid conversationId, int ordinal, AiMessage message) => new()
    {
        Id = message.Id,
        ConversationId = conversationId,
        Ordinal = ordinal,
        Role = message.Role.ToString(),
        Content = message.Content,
        CitationsJson = message.Citations.Count == 0
            ? null
            : JsonSerializer.Serialize(
                message.Citations.Select(c => new AiCitationJson(c.Text, c.Key, c.Path, c.StartLine, c.EndLine, c.ElementId, c.Verified, c.Problem)).ToList(),
                StorageJsonContext.Default.ListAiCitationJson),
        ContextManifestJson = message.Manifest is not { } manifest
            ? null
            : JsonSerializer.Serialize(
                new AiManifestJson(
                    [.. manifest.Entries.Select(e => new AiContextEntryJson(e.Key, e.Kind, e.Label, e.Path, e.StartLine, e.EndLine, e.ElementId, e.Tokens))],
                    manifest.BudgetTokens, manifest.UsedTokens, manifest.OmittedEntries),
                StorageJsonContext.Default.AiManifestJson),
        PromptTokens = message.PromptTokens,
        CompletionTokens = message.CompletionTokens,
        CreatedAt = message.CreatedAt,
    };
}
