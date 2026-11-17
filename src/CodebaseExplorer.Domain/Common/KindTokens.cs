using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;

namespace CodebaseExplorer.Domain.Common;

/// <summary>
/// Stable lowercase tokens for enum values that appear inside natural keys. Changing a token
/// changes element ids, so tokens are fixed once released — independent of enum member names.
/// </summary>
public static class KindTokens
{
    public static string ToKeyToken(this Ecosystem ecosystem) => ecosystem switch
    {
        Ecosystem.DotNet => "dotnet",
        Ecosystem.Node => "node",
        Ecosystem.Python => "python",
        Ecosystem.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(ecosystem), ecosystem, null),
    };

    public static string ToKeyToken(this ModuleKind kind) => kind switch
    {
        ModuleKind.Namespace => "namespace",
        ModuleKind.Directory => "directory",
        ModuleKind.Package => "package",
        ModuleKind.FileModule => "file",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string ToKeyToken(this PackageEcosystem ecosystem) => ecosystem switch
    {
        PackageEcosystem.NuGet => "nuget",
        PackageEcosystem.Npm => "npm",
        PackageEcosystem.PyPI => "pypi",
        PackageEcosystem.DotNetFramework => "dotnet-framework",
        PackageEcosystem.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(ecosystem), ecosystem, null),
    };

    public static string ToKeyToken(this RelationshipKind kind) => kind switch
    {
        RelationshipKind.ProjectReference => "project-reference",
        RelationshipKind.PackageReference => "package-reference",
        RelationshipKind.FrameworkReference => "framework-reference",
        RelationshipKind.Imports => "imports",
        RelationshipKind.Inherits => "inherits",
        RelationshipKind.Implements => "implements",
        RelationshipKind.UsesType => "uses-type",
        RelationshipKind.Instantiates => "instantiates",
        RelationshipKind.Calls => "calls",
        RelationshipKind.RegistersService => "registers-service",
        RelationshipKind.DependsOn => "depends-on",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
