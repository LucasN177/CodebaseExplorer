using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;

namespace CodebaseExplorer.Domain.Entities;

/// <summary>A named code element: type, function, member (DOMAIN_MODEL.md §4.6).</summary>
public sealed class CodeEntity
{
    private CodeEntity(
        CodeProject project,
        CodeModule module,
        EntityKind kind,
        string name,
        string qualifiedName,
        LanguageId language,
        string symbolKey,
        Visibility visibility,
        EntityModifiers modifiers,
        IReadOnlyList<SourceLocation> declarations,
        ElementId? containingEntityId,
        string? signature,
        EntityMetrics? metrics)
    {
        NaturalKey = NaturalKey.ForEntity(project.NaturalKey, language.Value, symbolKey);
        Id = ElementId.FromNaturalKey(NaturalKey);
        RepositoryId = project.RepositoryId;
        ProjectId = project.Id;
        ModuleId = module.Id;
        Kind = kind;
        Name = name;
        QualifiedName = qualifiedName;
        Language = language;
        SymbolKey = symbolKey;
        Visibility = visibility;
        Modifiers = modifiers;
        Declarations = declarations;
        ContainingEntityId = containingEntityId;
        Signature = signature;
        Metrics = metrics;
    }

    public ElementId Id { get; }

    public NaturalKey NaturalKey { get; }

    public RepositoryId RepositoryId { get; }

    public ElementId ProjectId { get; }

    public ElementId ModuleId { get; }

    public EntityKind Kind { get; }

    public string Name { get; }

    public string QualifiedName { get; }

    public LanguageId Language { get; }

    /// <summary>Language-specific symbol key (e.g. Roslyn documentation comment id), see DOMAIN_MODEL.md §3.2.</summary>
    public string SymbolKey { get; }

    public Visibility Visibility { get; }

    public EntityModifiers Modifiers { get; }

    /// <summary>At least one declaration; several for partial types.</summary>
    public IReadOnlyList<SourceLocation> Declarations { get; }

    /// <summary>Enclosing type for members and nested types.</summary>
    public ElementId? ContainingEntityId { get; }

    /// <summary>Display signature without bodies.</summary>
    public string? Signature { get; }

    public EntityMetrics? Metrics { get; }

    /// <exception cref="ArgumentException">An argument violates an entity invariant.</exception>
    public static CodeEntity Create(
        CodeProject project,
        CodeModule module,
        EntityKind kind,
        string name,
        string qualifiedName,
        LanguageId language,
        string symbolKey,
        IEnumerable<SourceLocation> declarations,
        Visibility visibility = Visibility.Unknown,
        EntityModifiers modifiers = EntityModifiers.None,
        CodeEntity? containingEntity = null,
        string? signature = null,
        EntityMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedName);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolKey);
        ArgumentNullException.ThrowIfNull(declarations);

        if (module.ProjectId != project.Id)
        {
            throw new ArgumentException("The module must belong to the entity's project.", nameof(module));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        if (!Enum.IsDefined(visibility))
        {
            throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null);
        }

        const EntityModifiers allModifiers = EntityModifiers.Abstract | EntityModifiers.Static | EntityModifiers.Sealed
            | EntityModifiers.Async | EntityModifiers.Partial | EntityModifiers.Generic | EntityModifiers.Generated;
        if ((modifiers & ~allModifiers) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modifiers), modifiers, null);
        }

        var declarationList = declarations.Distinct().ToArray();
        if (declarationList.Length == 0)
        {
            throw new ArgumentException("An entity needs at least one declaration location.", nameof(declarations));
        }

        if (containingEntity is not null && containingEntity.ProjectId != project.Id)
        {
            throw new ArgumentException("A containing entity must belong to the same project.", nameof(containingEntity));
        }

        var entity = new CodeEntity(
            project,
            module,
            kind,
            name.Trim(),
            qualifiedName.Trim(),
            language,
            symbolKey,
            visibility,
            modifiers,
            declarationList,
            containingEntity?.Id,
            string.IsNullOrWhiteSpace(signature) ? null : signature.Trim(),
            metrics);

        if (entity.ContainingEntityId == entity.Id)
        {
            throw new ArgumentException("An entity cannot contain itself.", nameof(containingEntity));
        }

        return entity;
    }
}
