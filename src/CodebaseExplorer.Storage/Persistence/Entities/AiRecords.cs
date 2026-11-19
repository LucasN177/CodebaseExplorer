namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>AIConversations</c> (§4.14).</summary>
internal sealed class AiConversationRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    /// <summary>Context left this machine (cloud provider or remote endpoint).</summary>
    public bool IsCloudProvider { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Table <c>AIMessages</c> (§4.15).</summary>
internal sealed class AiMessageRecord
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public int Ordinal { get; set; }

    /// <summary><c>User</c>, <c>Assistant</c> or <c>System</c>.</summary>
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? CitationsJson { get; set; }

    public string? ContextManifestJson { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Table <c>AIArtifacts</c> (§4.16): reusable AI outputs attached to model elements.</summary>
internal sealed class AiArtifactRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public Guid? MessageId { get; set; }

    public string ElementKind { get; set; } = string.Empty;

    public Guid ElementId { get; set; }

    /// <summary><c>ComponentExplanation</c> or <c>Summary</c>.</summary>
    public string ArtifactKind { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public Guid AnalysisRunId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
