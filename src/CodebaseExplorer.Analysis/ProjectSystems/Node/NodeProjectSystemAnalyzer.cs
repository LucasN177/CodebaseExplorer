using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Parsing;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.ProjectSystems.Node;

/// <summary>
/// Static Node project-system analyzer (<c>node.manifest</c>, ANALYSIS_ENGINE.md Stage 2): one project per
/// <c>package.json</c> (workspace roots from <c>workspaces</c> or <c>pnpm-workspace.yaml</c>), dependencies on
/// packages of the repository as <c>ProjectReference</c>, all others as npm <c>PackageReference</c>, and
/// <c>tsconfig.json</c> project references. Never runs npm, yarn, pnpm or any script: scripts are only listed.
/// </summary>
internal sealed class NodeProjectSystemAnalyzer : IProjectSystemAnalyzer
{
    public AnalyzerDescriptor Descriptor { get; } = new(NodeCodes.AnalyzerName, NodeCodes.Version);

    public bool AppliesTo(ScanResult scan) => scan.Files.Any(f => f.Marker == ProjectMarker.NodePackage);

    public async Task<AnalyzerFragment> AnalyzeAsync(ProjectSystemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = new FragmentBuilder(context.RepositoryId, context.AnalysisRunId, Descriptor);
        var existing = context.Files.Select(f => f.Path.Value).ToHashSet(StringComparer.Ordinal);

        // 1. Read every package.json (ordinal order, so names and duplicates resolve deterministically).
        var packages = new List<PackageInfo>();
        foreach (var file in context.Files.Where(f => f.Marker == ProjectMarker.NodePackage).OrderBy(f => f.Path.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            packages.Add(await ReadPackageAsync(context, file.Path, existing, result, cancellationToken));
        }

        // 2. Workspace membership: the innermost workspace root whose patterns match the package folder.
        var roots = packages.Where(p => p.WorkspacePatterns is not null).OrderByDescending(p => p.Directory.Value.Length).ToArray();
        foreach (var package in packages)
        {
            package.WorkspaceRoot = roots.FirstOrDefault(root => root != package
                && package.Directory.IsWithin(root.Directory)
                && root.WorkspacePatterns!.Matches(RelativeTo(root.Directory, package.Directory)));
        }

        // 3. Projects.
        var typeScriptOwners = TypeScriptOwners(context, packages);
        foreach (var package in packages)
        {
            package.Project = CreateProject(context, package, typeScriptOwners.Contains(package.Directory.Value));
            result.Projects.Add(package.Project);
        }

        var byName = new Dictionary<string, PackageInfo>(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            if (!byName.TryAdd(package.Name, package))
            {
                result.Report(DiagnosticSeverity.Warning, NodeCodes.DuplicateName,
                    $"{package.Manifest}: package name '{package.Name}' is also used by {byName[package.Name].Manifest}; dependencies resolve to the first.",
                    result.At(package.Manifest, 1, 1));
            }
        }

        // 4. Dependencies and tsconfig references.
        foreach (var package in packages)
        {
            AddDependencies(context, result, package, byName);
            await AddTsConfigReferencesAsync(context, result, package, packages, cancellationToken);
        }

        return result.Build();
    }

    private static async Task<PackageInfo> ReadPackageAsync(
        ProjectSystemContext context, RelativePath path, HashSet<string> existing, FragmentBuilder result, CancellationToken cancellationToken)
    {
        var directory = RepositoryPaths.DirectoryOf(path);
        var info = new PackageInfo(path, directory);
        var fallbackName = directory.IsRoot ? Path.GetFileName(Path.TrimEndingDirectorySeparator(context.Root.FullPath)) : directory.FileName;

        var json = await ReadJsonAsync(context, path, NodeCodes.ManifestInvalid, result, cancellationToken);
        if (json is JsonObject root)
        {
            var problems = new List<string>();
            info.Data = PackageManifest.From(root, problems);
            foreach (var problem in problems)
            {
                result.Report(DiagnosticSeverity.Info, NodeCodes.ManifestEntryIgnored, $"{path}: {problem}", result.At(path, 1, 1));
            }
        }
        else if (json is not null)
        {
            result.Report(DiagnosticSeverity.Warning, NodeCodes.ManifestInvalid, $"{path}: the file is not a JSON object.", result.At(path, 1, 1));
        }

        info.Name = info.Data?.Name ?? fallbackName;
        if (info.Data is { Name: null })
        {
            result.Report(DiagnosticSeverity.Info, NodeCodes.NameMissing,
                $"{path}: no package name; the project is named after its folder '{fallbackName}'.", result.At(path, 1, 1));
        }

        var patterns = info.Data?.Workspaces?.Select(w => w.Value).ToList();
        var pnpmPath = directory.IsRoot ? NodeCodes.PnpmWorkspace : $"{directory.Value}/{NodeCodes.PnpmWorkspace}";
        if (existing.Contains(pnpmPath))
        {
            var pnpm = RelativePath.Create(pnpmPath);
            var read = await context.FileReader.ReadAllTextAsync(context.Root, pnpm, context.Limits.MaxManifestBytes, cancellationToken);
            if (read.TryGetValue(out var content, out var error))
            {
                (patterns ??= []).AddRange(PnpmWorkspaceFile.ReadPackages(content).Select(p => p.Value));
            }
            else
            {
                result.Report(DiagnosticSeverity.Warning, NodeCodes.PnpmWorkspaceInvalid, $"{pnpm}: could not be read: {error.Message}", result.At(pnpm, 1, 1));
            }
        }

        info.WorkspacePatternList = patterns?.Distinct(StringComparer.Ordinal).ToList();
        info.WorkspacePatterns = patterns is null ? null : new WorkspacePatterns(patterns);
        return info;
    }

    private static async Task<JsonValue?> ReadJsonAsync(
        ProjectSystemContext context, RelativePath path, string invalidCode, FragmentBuilder result, CancellationToken cancellationToken)
    {
        var read = await context.FileReader.ReadAllTextAsync(context.Root, path, context.Limits.MaxManifestBytes, cancellationToken);
        if (!read.TryGetValue(out var content, out var readError))
        {
            result.Report(DiagnosticSeverity.Warning, invalidCode, $"{path}: could not be read: {readError.Message} ({readError.Code})", result.At(path, 1, 1));
            return null;
        }

        if (!SafeJson.TryParse(content, out var value, out var parseError))
        {
            result.Report(DiagnosticSeverity.Warning, invalidCode, $"{path}: {parseError}", result.At(path, 1, 1));
            return null;
        }

        return value;
    }

    /// <summary>Package folders that contain TypeScript: a <c>tsconfig.json</c> or an owned <c>.ts</c>/<c>.tsx</c> file.</summary>
    private static HashSet<string> TypeScriptOwners(ProjectSystemContext context, List<PackageInfo> packages)
    {
        var owners = packages.OrderByDescending(p => p.Directory.Value.Length).ToArray();
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in context.Files)
        {
            var isTypeScript = file.Language == LanguageId.TypeScript || file.Language == LanguageId.Tsx;
            if (isTypeScript || file.Path.FileName == NodeCodes.TsConfigJson)
            {
                var owner = owners.FirstOrDefault(p => file.Path.IsWithin(p.Directory));
                if (owner is not null && (isTypeScript || RepositoryPaths.DirectoryOf(file.Path) == owner.Directory))
                {
                    result.Add(owner.Directory.Value);
                }
            }
        }

        return result;
    }

    private static CodeProject CreateProject(ProjectSystemContext context, PackageInfo package, bool isTypeScript)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        var data = package.Data;
        if (data?.Version is { } version)
        {
            properties[NodeCodes.PropertyVersion] = version;
        }

        if (data?.IsPrivate == true)
        {
            properties[NodeCodes.PropertyPrivate] = "true";
        }

        if (package.WorkspacePatternList is { Count: > 0 } patterns)
        {
            properties[NodeCodes.PropertyWorkspaces] = string.Join(';', patterns);
        }

        if (package.WorkspaceRoot is { } root)
        {
            properties[NodeCodes.PropertyWorkspace] = root.Name;
        }

        if (data is { Scripts.Count: > 0 })
        {
            properties[NodeCodes.PropertyScripts] = string.Join(';', data.Scripts.Select(s => s.Value).Order(StringComparer.Ordinal));
            var lifecycle = data.Scripts.Select(s => s.Value).Where(PackageManifest.LifecycleScripts.Contains).Order(StringComparer.Ordinal).ToArray();
            if (lifecycle.Length > 0)
            {
                properties[NodeCodes.PropertyLifecycleScripts] = string.Join(';', lifecycle);
            }
        }

        var tsconfig = TsConfigPath(package);
        if (context.Files.Any(f => f.Path.Value == tsconfig.Value))
        {
            properties[NodeCodes.PropertyTsConfig] = tsconfig.Value;
        }

        return CodeProject.Create(
            context.RepositoryId,
            package.Name,
            package.WorkspacePatterns is null ? ProjectKind.NodePackage : ProjectKind.NodeWorkspaceRoot,
            Ecosystem.Node,
            isTypeScript ? LanguageId.TypeScript : LanguageId.JavaScript,
            package.Manifest,
            package.Directory,
            properties);
    }

    private static void AddDependencies(ProjectSystemContext context, FragmentBuilder result, PackageInfo package, Dictionary<string, PackageInfo> byName)
    {
        var data = package.Data;
        if (data is null)
        {
            return;
        }

        foreach (var script in data.Scripts.Where(s => PackageManifest.LifecycleScripts.Contains(s.Value)))
        {
            result.Report(DiagnosticSeverity.Info, NodeCodes.LifecycleNotExecuted,
                $"{package.Manifest}: lifecycle script '{script.Value}' is listed only; Codebase Explorer never runs package scripts.",
                result.At(package.Manifest, script.Line, script.Column));
        }

        var source = new ElementReference(ElementKind.Project, package.Project!.Id);
        foreach (var dependency in data.PackageDependencies)
        {
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dependencyType"] = dependency.DependencyType,
            };
            if (dependency.VersionSpec.Length > 0)
            {
                attributes["versionSpec"] = dependency.VersionSpec;
            }

            if (dependency.IsDevOnly)
            {
                attributes["devOnly"] = "true";
            }

            var location = result.At(package.Manifest, dependency.Line, dependency.Column);
            var rule = NodeCodes.RuleFor(dependency.DependencyType);

            if (byName.TryGetValue(dependency.Name, out var target))
            {
                if (target != package)
                {
                    result.AddRelationship(source, RelationshipKind.ProjectReference, new ElementReference(ElementKind.Project, target.Project!.Id),
                        EvidenceLevel.Confirmed, rule, location, attributes);
                }

                continue;
            }

            var external = ExternalDependency.Create(context.RepositoryId, PackageEcosystem.Npm, dependency.Name);
            result.ExternalDependencies.Add(external);
            result.AddRelationship(source, RelationshipKind.PackageReference, new ElementReference(ElementKind.ExternalDependency, external.Id),
                EvidenceLevel.Confirmed, rule, location, attributes);
        }
    }

    private static async Task AddTsConfigReferencesAsync(
        ProjectSystemContext context, FragmentBuilder result, PackageInfo package, List<PackageInfo> packages, CancellationToken cancellationToken)
    {
        if (!package.Project!.Properties.TryGetValue(NodeCodes.PropertyTsConfig, out var tsconfigPath))
        {
            return;
        }

        var path = RelativePath.Create(tsconfigPath);
        var read = await context.FileReader.ReadAllTextAsync(context.Root, path, context.Limits.MaxManifestBytes, cancellationToken);
        if (!read.TryGetValue(out var content, out var readError))
        {
            result.Report(DiagnosticSeverity.Warning, NodeCodes.TsConfigInvalid, $"{path}: could not be read: {readError.Message}", result.At(path, 1, 1));
            return;
        }

        if (!TsConfigFile.TryParse(content, out var config, out var parseError))
        {
            result.Report(DiagnosticSeverity.Warning, NodeCodes.TsConfigInvalid, $"{path}: {parseError}", result.At(path, 1, 1));
            return;
        }

        if (config!.IsSolutionStyle)
        {
            return; // groups projects, is not a dependency
        }

        var source = new ElementReference(ElementKind.Project, package.Project.Id);
        foreach (var reference in config.References)
        {
            var location = result.At(path, reference.Line, reference.Column);
            var resolved = RepositoryPaths.Resolve(package.Directory, reference.Value);
            if (resolved is { } target && target.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                resolved = RepositoryPaths.DirectoryOf(target);
            }

            var referenced = resolved is { } directory ? packages.FirstOrDefault(p => p.Directory == directory) : null;
            if (referenced is null)
            {
                result.Report(DiagnosticSeverity.Warning, NodeCodes.TsConfigReferenceUnresolved,
                    $"{path}: referenced project '{reference.Value}' is not a package of the repository.", location);
                continue;
            }

            if (referenced != package)
            {
                result.AddRelationship(source, RelationshipKind.ProjectReference, new ElementReference(ElementKind.Project, referenced.Project!.Id),
                    EvidenceLevel.Confirmed, NodeCodes.RuleTsConfigReference, location);
            }
        }
    }

    private static RelativePath TsConfigPath(PackageInfo package) =>
        package.Directory.IsRoot ? RelativePath.Create(NodeCodes.TsConfigJson) : RelativePath.Create($"{package.Directory.Value}/{NodeCodes.TsConfigJson}");

    private static string RelativeTo(RelativePath root, RelativePath directory) =>
        root.IsRoot ? directory.Value : directory.Value[(root.Value.Length + 1)..];

    private sealed class PackageInfo(RelativePath manifest, RelativePath directory)
    {
        public RelativePath Manifest { get; } = manifest;

        public RelativePath Directory { get; } = directory;

        public string Name { get; set; } = string.Empty;

        public PackageManifest? Data { get; set; }

        public List<string>? WorkspacePatternList { get; set; }

        public WorkspacePatterns? WorkspacePatterns { get; set; }

        public PackageInfo? WorkspaceRoot { get; set; }

        public CodeProject? Project { get; set; }
    }
}
