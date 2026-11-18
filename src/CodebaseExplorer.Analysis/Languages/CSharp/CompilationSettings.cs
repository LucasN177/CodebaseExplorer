using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodebaseExplorer.Analysis.Languages.CSharp;

/// <summary>
/// How a project's C# compilation is set up from statically known MSBuild properties — without MSBuild
/// evaluation, restore or any repository-defined code (ADR-004).
/// </summary>
/// <remarks>
/// Metadata references are always the same embedded .NET 10 reference assemblies
/// (<c>Basic.Reference.Assemblies.Net100</c>), never assemblies from the machine or the repository, so
/// that symbol keys — and therefore entity ids — are identical on every machine (ADR-004 amendment,
/// verification item V-1 of fixture <c>dotnet-advanced</c>). NuGet package types stay unresolved.
/// </remarks>
internal static class CompilationSettings
{
    /// <summary>Global usings the .NET SDK generates for <c>ImplicitUsings=enable</c> (Microsoft.NET.Sdk).</summary>
    private static readonly string[] SdkImplicitUsings =
    [
        "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks",
    ];

    /// <summary>Additional global usings of Microsoft.NET.Sdk.Web.</summary>
    private static readonly string[] WebSdkImplicitUsings =
    [
        "System.Net.Http.Json", "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Routing", "Microsoft.Extensions.Configuration", "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting", "Microsoft.Extensions.Logging",
    ];

    public static ImmutableArray<PortableExecutableReference> FrameworkReferences { get; } = [.. Basic.Reference.Assemblies.Net100.References.All];

    public static CSharpParseOptions ParseOptions(IReadOnlyDictionary<string, string> properties)
    {
        // DefineConstants as statically resolved, plus the symbols every modern .NET target defines.
        var symbols = (Property(properties, "DefineConstants") ?? string.Empty)
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !s.Contains('$', StringComparison.Ordinal))
            .Concat(["NET", "NETCOREAPP"])
            .Distinct(StringComparer.Ordinal);

        return new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None, SourceCodeKind.Regular, symbols);
    }

    /// <summary>The generated global usings file, or <see langword="null"/> when implicit usings are off.</summary>
    public static string? ImplicitUsingsSource(IReadOnlyDictionary<string, string> properties)
    {
        var implicitUsings = Property(properties, "ImplicitUsings");
        if (!string.Equals(implicitUsings, "enable", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(implicitUsings, "true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var usings = string.Equals(Property(properties, "Sdk"), "Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
            ? SdkImplicitUsings.Concat(WebSdkImplicitUsings)
            : SdkImplicitUsings;
        return string.Concat(usings.Select(u => $"global using global::{u};\n"));
    }

    public static CSharpCompilationOptions CompilationOptions(IReadOnlyDictionary<string, string> properties) =>
        new(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: string.Equals(Property(properties, "Nullable"), "enable", StringComparison.OrdinalIgnoreCase)
                ? NullableContextOptions.Enable
                : NullableContextOptions.Disable,
            concurrentBuild: false);

    private static string? Property(IReadOnlyDictionary<string, string> properties, string name) =>
        properties.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}
