namespace CodebaseExplorer.Domain.Relationships;

/// <summary>Kind of a directed dependency (DOMAIN_MODEL.md §5.2).</summary>
public enum RelationshipKind
{
    ProjectReference,
    PackageReference,
    FrameworkReference,
    Imports,
    Inherits,
    Implements,
    UsesType,
    Instantiates,
    Calls,
    RegistersService,
    DependsOn,
}
