namespace CodebaseExplorer.Domain.Common;

/// <summary>Kind of a model element that can be the endpoint of a relationship.</summary>
public enum ElementKind
{
    File,
    Project,
    Module,
    Entity,
    ExternalDependency,
}
