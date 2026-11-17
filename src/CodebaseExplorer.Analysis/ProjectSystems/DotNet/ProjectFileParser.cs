using System.Xml.Linq;
using CodebaseExplorer.Analysis.Parsing;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>
/// Static evaluation of SDK-style project files (ANALYSIS_ENGINE.md Stage 2). Never uses MSBuild:
/// the nearest <c>Directory.Build.props</c> and <c>Directory.Packages.props</c> inside the repository
/// are applied in MSBuild order (props → packages props → project); properties are evaluated in a
/// first pass and items in a second pass with the final property values, as MSBuild does.
/// </summary>
/// <remarks>
/// Limits (reported as diagnostics, never guessed): only the nearest <c>Directory.*.props</c> is
/// applied (chained imports use property functions); other <c>&lt;Import&gt;</c> elements, targets,
/// tasks, property functions, item lists and conditions outside <c>'…' == '…'</c> / <c>!=</c> are not
/// evaluated; <c>PackageReference Update</c> items are ignored.
/// </remarks>
internal static class ProjectFileParser
{
    private const string DirectoryBuildProps = "Directory.Build.props";
    private const string DirectoryPackagesProps = "Directory.Packages.props";

    public static async Task<ProjectFileModel> ParseAsync(
        RepositoryId repositoryId,
        RelativePath projectPath,
        IMsBuildFileSource source,
        long maxCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var context = new EvaluationContext(repositoryId, projectPath);

        var project = await LoadAsync(context, projectPath, source, maxCharacters, cancellationToken);
        if (project is null)
        {
            return context.Build(sdk: null, isValid: false);
        }

        var documents = new List<(RelativePath Path, XElement Root)>();
        foreach (var importName in new[] { DirectoryBuildProps, DirectoryPackagesProps })
        {
            if (FindNearest(projectPath, importName, source) is { } importPath
                && await LoadAsync(context, importPath, source, maxCharacters, cancellationToken) is { } imported)
            {
                documents.Add((importPath, imported));
                context.ImportedFiles.Add(importPath);
            }
        }

        documents.Add((projectPath, project));

        context.Properties["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(projectPath.FileName);
        context.Properties["MSBuildProjectFile"] = projectPath.FileName;
        context.Properties["MSBuildProjectExtension"] = Path.GetExtension(projectPath.FileName);

        // Pass 1: properties, in import order.
        foreach (var (path, root) in documents)
        {
            EvaluateProperties(context, path, root);
        }

        // Pass 2: items, with final property values.
        var packageVersions = new Dictionary<string, (string Version, MsBuildItemLocation Location)>(StringComparer.OrdinalIgnoreCase);
        var packageReferences = new List<(string Name, string? Version, string? Override, MsBuildItemLocation Location)>();
        foreach (var (path, root) in documents)
        {
            EvaluateItems(context, path, root, packageVersions, packageReferences);
        }

        ResolvePackageVersions(context, packageVersions, packageReferences);

        return context.Build(Sdk(project), isValid: true);
    }

    private static async Task<XElement?> LoadAsync(
        EvaluationContext context, RelativePath path, IMsBuildFileSource source, long maxCharacters, CancellationToken cancellationToken)
    {
        var (content, error) = await source.LoadAsync(path, cancellationToken);
        if (content is null)
        {
            context.Warn(DotNetCodes.InvalidXml, $"{path}: could not be read: {error}", path, 1, 1);
            return null;
        }

        if (!SafeXml.TryLoad(content, maxCharacters, out var document, out var xmlError) || document!.Root is null)
        {
            context.Warn(DotNetCodes.InvalidXml, $"{path}: invalid XML ({xmlError ?? "no root element"}).", path, 1, 1);
            return null;
        }

        if (document.Root.Name.LocalName != "Project")
        {
            context.Warn(DotNetCodes.InvalidXml, $"{path}: root element is not <Project>.", path, 1, 1);
            return null;
        }

        return document.Root;
    }

    private static RelativePath? FindNearest(RelativePath projectPath, string fileName, IMsBuildFileSource source)
    {
        var directory = RepositoryPaths.DirectoryOf(projectPath);
        while (true)
        {
            var candidate = directory.IsRoot ? RelativePath.Create(fileName) : RelativePath.Create($"{directory.Value}/{fileName}");
            if (source.Exists(candidate))
            {
                return candidate;
            }

            if (directory.IsRoot)
            {
                return null;
            }

            directory = RepositoryPaths.DirectoryOf(directory);
        }
    }

    private static void EvaluateProperties(EvaluationContext context, RelativePath path, XElement root)
    {
        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "PropertyGroup" when context.ConditionHolds(element, path):
                    foreach (var property in element.Elements())
                    {
                        if (!context.ConditionHolds(property, path))
                        {
                            continue;
                        }

                        var value = MsBuildExpressions.Expand(property.Value.Trim(), context.Lookup, out var problem);
                        if (value is null)
                        {
                            context.Properties.Remove(property.Name.LocalName);
                            context.Info(DotNetCodes.UnresolvedProperty,
                                $"{path}: property '{property.Name.LocalName}' is unknown ({problem}).", path, property);
                        }
                        else
                        {
                            context.Properties[property.Name.LocalName] = value;
                        }
                    }

                    break;

                case "Import":
                    var (line, column) = SafeXml.PositionOf(element);
                    context.Info(DotNetCodes.ImportNotEvaluated,
                        $"{path}:{line}: <Import Project=\"{(string?)element.Attribute("Project")}\"> is not evaluated.", path, line, column);
                    break;
            }
        }
    }

    private static void EvaluateItems(
        EvaluationContext context,
        RelativePath path,
        XElement root,
        Dictionary<string, (string Version, MsBuildItemLocation Location)> packageVersions,
        List<(string Name, string? Version, string? Override, MsBuildItemLocation Location)> packageReferences)
    {
        foreach (var group in root.Elements().Where(e => e.Name.LocalName == "ItemGroup"))
        {
            if (!context.ConditionHolds(group, path))
            {
                continue;
            }

            foreach (var item in group.Elements())
            {
                if (!context.ConditionHolds(item, path))
                {
                    continue;
                }

                var (line, column) = SafeXml.PositionOf(item);
                var location = new MsBuildItemLocation(path, line, column);
                var include = Attribute(context, item, "Include", path);

                switch (item.Name.LocalName)
                {
                    case "ProjectReference" when include is not null:
                        var resolved = RepositoryPaths.Resolve(RepositoryPaths.DirectoryOf(path), include);
                        if (resolved is null or { IsRoot: true })
                        {
                            context.Warn(DotNetCodes.PathOutsideRepository,
                                $"{location}: project reference '{include}' lies outside the repository.", path, line, column);
                            resolved = null;
                        }

                        context.ProjectReferences.Add(new ProjectReferenceItem(include, resolved, location));
                        break;

                    case "PackageReference" when include is not null:
                        packageReferences.Add((include, AttributeOrChild(context, item, "Version", path),
                            AttributeOrChild(context, item, "VersionOverride", path), location));
                        break;

                    case "PackageVersion" when include is not null:
                        if (AttributeOrChild(context, item, "Version", path) is { } version)
                        {
                            packageVersions[include] = (version, location);
                        }

                        break;

                    case "FrameworkReference" when include is not null:
                        context.FrameworkReferences.Add(new FrameworkReferenceItem(include, location));
                        break;
                }
            }
        }
    }

    private static void ResolvePackageVersions(
        EvaluationContext context,
        Dictionary<string, (string Version, MsBuildItemLocation Location)> packageVersions,
        List<(string Name, string? Version, string? Override, MsBuildItemLocation Location)> packageReferences)
    {
        var central = string.Equals(context.Lookup("ManagePackageVersionsCentrally"), "true", StringComparison.OrdinalIgnoreCase);

        foreach (var (name, version, versionOverride, location) in packageReferences)
        {
            PackageReferenceItem reference;
            if (versionOverride is not null)
            {
                reference = new(name, versionOverride, PackageVersionSource.Override, null, location);
            }
            else if (central && packageVersions.TryGetValue(name, out var centralVersion))
            {
                reference = new(name, centralVersion.Version, PackageVersionSource.Central, centralVersion.Location, location);
            }
            else if (version is not null)
            {
                reference = new(name, version, PackageVersionSource.Reference, null, location);
            }
            else
            {
                reference = new(name, null, PackageVersionSource.None, null, location);
                context.Warn(DotNetCodes.MissingPackageVersion,
                    $"{location}: no version found for package '{name}'.", location.File, location.Line, location.Column);
            }

            context.PackageReferences.Add(reference);
        }
    }

    private static string? Attribute(EvaluationContext context, XElement element, string name, RelativePath path)
    {
        var raw = (string?)element.Attribute(name);
        return raw is null ? null : context.ExpandOrReport(raw, element, name, path);
    }

    private static string? AttributeOrChild(EvaluationContext context, XElement element, string name, RelativePath path)
    {
        var raw = (string?)element.Attribute(name)
            ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
        return string.IsNullOrWhiteSpace(raw) ? null : context.ExpandOrReport(raw.Trim(), element, name, path);
    }

    private static string? Sdk(XElement project) =>
        (string?)project.Attribute("Sdk")
        ?? (string?)project.Elements().FirstOrDefault(e => e.Name.LocalName == "Sdk")?.Attribute("Name");

    /// <summary>Mutable state of one evaluation.</summary>
    private sealed class EvaluationContext(RepositoryId repositoryId, RelativePath projectPath)
    {
        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<RelativePath> ImportedFiles { get; } = [];

        public List<ProjectReferenceItem> ProjectReferences { get; } = [];

        public List<PackageReferenceItem> PackageReferences { get; } = [];

        public List<FrameworkReferenceItem> FrameworkReferences { get; } = [];

        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public string? Lookup(string name) => Properties.GetValueOrDefault(name);

        public bool ConditionHolds(XElement element, RelativePath path)
        {
            var condition = (string?)element.Attribute("Condition");
            var result = MsBuildExpressions.EvaluateCondition(condition, Lookup, out var problem);
            if (result == ConditionResult.Unknown)
            {
                Warn(DotNetCodes.UnsupportedCondition,
                    $"{path}: condition \"{condition}\" on <{element.Name.LocalName}> is not evaluated ({problem}); the element is skipped.",
                    path, element);
            }

            return result == ConditionResult.True;
        }

        public string? ExpandOrReport(string raw, XElement element, string attribute, RelativePath path)
        {
            var value = MsBuildExpressions.Expand(raw, Lookup, out var problem);
            if (value is null)
            {
                Warn(DotNetCodes.UnresolvedProperty,
                    $"{path}: {attribute} \"{raw}\" on <{element.Name.LocalName}> is unknown ({problem}); the element is skipped.",
                    path, element);
            }

            return value;
        }

        public void Info(string code, string message, RelativePath path, XElement element)
        {
            var (line, column) = SafeXml.PositionOf(element);
            Info(code, message, path, line, column);
        }

        public void Info(string code, string message, RelativePath path, int line, int column) =>
            Diagnostics.Add(Diagnostic(DiagnosticSeverity.Info, code, message, path, line, column));

        public void Warn(string code, string message, RelativePath path, XElement element)
        {
            var (line, column) = SafeXml.PositionOf(element);
            Warn(code, message, path, line, column);
        }

        public void Warn(string code, string message, RelativePath path, int line, int column) =>
            Diagnostics.Add(Diagnostic(DiagnosticSeverity.Warning, code, message, path, line, column));

        public ProjectFileModel Build(string? sdk, bool isValid) => new(
            projectPath,
            sdk,
            Properties
                .Where(p => !p.Key.StartsWith("MSBuildProject", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase)
                .AsReadOnly(),
            ImportedFiles,
            ProjectReferences,
            PackageReferences,
            FrameworkReferences,
            Diagnostics,
            isValid);

        private AnalysisDiagnostic Diagnostic(DiagnosticSeverity severity, string code, string message, RelativePath path, int line, int column) =>
            new(severity, code, message, DotNetCodes.Analyzer,
                SourceLocation.Line(RepositoryFile.IdFor(repositoryId, path), Math.Max(1, line), Math.Max(1, column)));
    }
}
