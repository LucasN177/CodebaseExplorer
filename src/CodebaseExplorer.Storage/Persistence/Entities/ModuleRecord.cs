using CodebaseExplorer.Domain.Modules;

namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>Modules</c> (§4.6).</summary>
internal sealed class ModuleRecord
{
    public Guid Id { get; set; }

    public Guid RepositoryId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? ParentModuleId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string QualifiedName { get; set; } = string.Empty;

    public string NaturalKey { get; set; } = string.Empty;

    public ModuleKind Kind { get; set; }

    public Guid LastSeenRunId { get; set; }
}
