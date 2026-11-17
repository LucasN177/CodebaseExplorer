using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>How the version of a package reference was determined.</summary>
internal enum PackageVersionSource
{
    /// <summary><c>Version</c> on the reference itself (no central package management).</summary>
    Reference,

    /// <summary>A <c>PackageVersion</c> item (central package management).</summary>
    Central,

    /// <summary><c>VersionOverride</c> on the reference.</summary>
    Override,

    /// <summary>No version could be determined.</summary>
    None,
}

/// <summary>A <c>ProjectReference</c> item.</summary>
/// <param name="Include">The <c>Include</c> value as written.</param>
/// <param name="ResolvedPath">The referenced project file; <see langword="null"/> if it resolves outside the repository or contains unknown values.</param>
/// <param name="Location">Declaration of the item.</param>
internal sealed record ProjectReferenceItem(string Include, RelativePath? ResolvedPath, MsBuildItemLocation Location);

/// <summary>A <c>PackageReference</c> item.</summary>
/// <param name="Name">Package id as written.</param>
/// <param name="VersionSpec">Version or range; <see langword="null"/> if unknown.</param>
/// <param name="VersionSource">Where the version came from.</param>
/// <param name="VersionLocation">Declaration of the <c>PackageVersion</c> item for <see cref="PackageVersionSource.Central"/>.</param>
/// <param name="Location">Declaration of the reference.</param>
internal sealed record PackageReferenceItem(
    string Name,
    string? VersionSpec,
    PackageVersionSource VersionSource,
    MsBuildItemLocation? VersionLocation,
    MsBuildItemLocation Location);

/// <summary>A <c>FrameworkReference</c> item.</summary>
internal sealed record FrameworkReferenceItem(string Name, MsBuildItemLocation Location);

/// <summary>
/// Statically evaluated content of one project file, including the nearest <c>Directory.Build.props</c>
/// and <c>Directory.Packages.props</c> (ANALYSIS_ENGINE.md Stage 2).
/// </summary>
/// <param name="ProjectPath">The project file.</param>
/// <param name="Sdk">Value of the <c>Sdk</c> attribute (or first <c>&lt;Sdk Name&gt;</c>), if any.</param>
/// <param name="Properties">Statically known property values after all imports (case-insensitive keys).</param>
/// <param name="ImportedFiles">The <c>Directory.*.props</c> files that were applied, in evaluation order.</param>
/// <param name="ProjectReferences">Project references in declaration order.</param>
/// <param name="PackageReferences">Package references in declaration order.</param>
/// <param name="FrameworkReferences">Framework references in declaration order.</param>
/// <param name="Diagnostics">Problems found while evaluating.</param>
/// <param name="IsValid">False if the project file itself could not be loaded or parsed.</param>
internal sealed record ProjectFileModel(
    RelativePath ProjectPath,
    string? Sdk,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<RelativePath> ImportedFiles,
    IReadOnlyList<ProjectReferenceItem> ProjectReferences,
    IReadOnlyList<PackageReferenceItem> PackageReferences,
    IReadOnlyList<FrameworkReferenceItem> FrameworkReferences,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics,
    bool IsValid)
{
    /// <summary>Target frameworks from <c>TargetFrameworks</c>, else <c>TargetFramework</c> (empty if unknown).</summary>
    public IReadOnlyList<string> TargetFrameworks =>
        (Properties.GetValueOrDefault("TargetFrameworks") is { Length: > 0 } multiple
            ? multiple
            : Properties.GetValueOrDefault("TargetFramework") ?? string.Empty)
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
