using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Projects;

namespace CodebaseExplorer.Domain.Modules;

/// <summary>
/// A logical grouping below a project: C# namespace, TS/JS directory module, Python package
/// (DOMAIN_MODEL.md §4.5).
/// </summary>
public sealed class CodeModule
{
    private CodeModule(CodeProject project, ModuleKind kind, string name, string qualifiedName, ElementId? parentModuleId)
    {
        NaturalKey = NaturalKey.ForModule(project.NaturalKey, kind.ToKeyToken(), qualifiedName);
        Id = ElementId.FromNaturalKey(NaturalKey);
        RepositoryId = project.RepositoryId;
        ProjectId = project.Id;
        Kind = kind;
        Name = name;
        QualifiedName = qualifiedName;
        ParentModuleId = parentModuleId;
    }

    public ElementId Id { get; }

    public NaturalKey NaturalKey { get; }

    public RepositoryId RepositoryId { get; }

    public ElementId ProjectId { get; }

    public ModuleKind Kind { get; }

    public string Name { get; }

    /// <summary>E.g. <c>Billing.Invoices</c>, <c>src/billing</c>, <c>billing.invoices</c>.</summary>
    public string QualifiedName { get; }

    /// <summary>Enclosing module (namespace nesting, parent folder); <see langword="null"/> for top-level modules.</summary>
    public ElementId? ParentModuleId { get; }

    /// <exception cref="ArgumentException">An argument violates a module invariant.</exception>
    public static CodeModule Create(
        CodeProject project,
        ModuleKind kind,
        string name,
        string qualifiedName,
        CodeModule? parent = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedName);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        if (parent is not null)
        {
            if (parent.ProjectId != project.Id)
            {
                throw new ArgumentException("A parent module must belong to the same project.", nameof(parent));
            }

            if (parent.QualifiedName == qualifiedName && parent.Kind == kind)
            {
                throw new ArgumentException("A module cannot be its own parent.", nameof(parent));
            }
        }

        return new CodeModule(project, kind, name.Trim(), qualifiedName.Trim(), parent?.Id);
    }
}
