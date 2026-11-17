using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;

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
}
