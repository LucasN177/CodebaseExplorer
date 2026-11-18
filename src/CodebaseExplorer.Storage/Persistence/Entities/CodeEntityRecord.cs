using CodebaseExplorer.Domain.Entities;

namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>CodeEntities</c> (§4.7).</summary>
internal sealed class CodeEntityRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ModuleId { get; set; }

    public Guid? ContainingEntityId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string QualifiedName { get; set; } = string.Empty;

    public string NaturalKey { get; set; } = string.Empty;

    /// <summary>Language-specific symbol key the id is derived from (addition in STORE-002, needed to rebuild the entity).</summary>
    public string SymbolKey { get; set; } = string.Empty;

    public EntityKind Kind { get; set; }

    public string Language { get; set; } = string.Empty;

    public Visibility Visibility { get; set; }

    /// <summary>Flags, stored as INTEGER.</summary>
    public EntityModifiers Modifiers { get; set; }

    public string? Signature { get; set; }

    public int? LineCount { get; set; }

    public int? MemberCount { get; set; }

    public Guid LastSeenRunId { get; set; }

    public List<CodeEntityDeclarationRecord> Declarations { get; set; } = [];
}
