using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.ProjectSystems.Python;

/// <summary>
/// Static Python project-system analyzer (<c>python.manifest</c>, ANALYSIS_ENGINE.md Stage 2): one project per
/// folder with Python manifests (<c>pyproject.toml</c>, <c>setup.cfg</c>, <c>setup.py</c>, <c>requirements*.txt</c>),
/// declared dependencies as PyPI <c>PackageReference</c>s (or <c>ProjectReference</c>s to Python projects of the
/// repository). <c>setup.py</c> is detected and reported, <b>never executed</b>; nothing is installed or fetched.
/// </summary>
internal sealed class PythonProjectSystemAnalyzer : IProjectSystemAnalyzer
{
    private const int MaxIncludeDepth = 5;

    private static readonly ProjectMarker[] Markers =
        [ProjectMarker.PythonProject, ProjectMarker.PythonSetupConfig, ProjectMarker.PythonSetupScript, ProjectMarker.PythonRequirements];

    public AnalyzerDescriptor Descriptor { get; } = new(PythonCodes.AnalyzerName, PythonCodes.Version);

    public bool AppliesTo(ScanResult scan) => scan.Files.Any(f => f.Marker is { } marker && Markers.Contains(marker));

    public async Task<AnalyzerFragment> AnalyzeAsync(ProjectSystemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = new FragmentBuilder(context.RepositoryId, context.AnalysisRunId, Descriptor);
        var existing = context.Files.Select(f => f.Path.Value).ToHashSet(StringComparer.Ordinal);

        var folders = context.Files
            .Where(f => f.Marker is { } marker && Markers.Contains(marker))
            .GroupBy(f => RepositoryPaths.DirectoryOf(f.Path))
            .OrderBy(g => g.Key.Value, StringComparer.Ordinal)
            .ToArray();

        var projects = new List<(CodeProject Project, List<PythonDependency> Dependencies)>();
        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifests = folder.Select(f => f.Path).OrderBy(Priority).ThenBy(p => p.Value, StringComparer.Ordinal).ToArray();
            var facts = new List<PythonManifestFacts>();
            foreach (var manifest in manifests)
            {
                facts.Add(await ReadManifestAsync(context, manifest, existing, result, cancellationToken));
            }

            var dependencies = facts.SelectMany(f => f.Dependencies).ToList();
            foreach (var problem in facts.SelectMany(f => f.Problems))
            {
                result.Report(problem.Severity, problem.Code, problem.Message, result.At(problem.File, problem.Line, problem.Column));
            }

            var project = CreateProject(context, folder.Key, manifests, facts);
            result.Projects.Add(project);
            projects.Add((project, dependencies));
        }

        var byName = new Dictionary<string, CodeProject>(StringComparer.Ordinal);
        foreach (var (project, _) in projects)
        {
            byName.TryAdd(NormalizedName(project.Name), project);
        }

        foreach (var (project, dependencies) in projects)
        {
            AddDependencies(context, result, project, dependencies, byName);
        }

        return result.Build();
    }

    /// <summary>Manifest precedence: the first one names the project and is its manifest file.</summary>
    private static int Priority(RelativePath path) => path.FileName switch
    {
        PythonCodes.PyProjectToml => 0,
        PythonCodes.SetupCfg => 1,
        PythonCodes.SetupPy => 2,
        PythonCodes.RequirementsTxt => 3,
        _ => 4,
    };

    private static async Task<PythonManifestFacts> ReadManifestAsync(
        ProjectSystemContext context, RelativePath manifest, HashSet<string> existing, FragmentBuilder result, CancellationToken cancellationToken)
    {
        if (manifest.FileName == PythonCodes.SetupPy)
        {
            result.Report(DiagnosticSeverity.Info, PythonCodes.SetupPyNotExecuted,
                $"{manifest}: setup.py is a program; it is detected but never executed, so dependencies declared only there are not known.",
                result.At(manifest, 1, 1));
            return new PythonManifestFacts();
        }

        var content = await ReadAsync(context, manifest, result, cancellationToken);
        if (content is null)
        {
            return new PythonManifestFacts();
        }

        return manifest.FileName switch
        {
            PythonCodes.PyProjectToml => PyProjectFile.Read(manifest, content),
            PythonCodes.SetupCfg => SetupCfgFile.Read(manifest, content),
            _ => await ReadRequirementsAsync(context, manifest, content, existing, result, [manifest.Value], cancellationToken),
        };
    }

    private static async Task<PythonManifestFacts> ReadRequirementsAsync(
        ProjectSystemContext context,
        RelativePath file,
        string content,
        HashSet<string> existing,
        FragmentBuilder result,
        List<string> chain,
        CancellationToken cancellationToken)
    {
        var includes = new List<RequirementsInclude>();
        var facts = RequirementsFile.Read(file, content, includes);

        foreach (var include in includes)
        {
            var location = result.At(file, include.Line, 1);
            var target = RepositoryPaths.Resolve(RepositoryPaths.DirectoryOf(file), include.Path);
            if (target is not { } path)
            {
                result.Report(DiagnosticSeverity.Warning, PythonCodes.IncludeOutsideRepository,
                    $"{file}:{include.Line}: '{include.Path}' lies outside the repository and is not read.", location);
                continue;
            }

            if (!existing.Contains(path.Value))
            {
                result.Report(DiagnosticSeverity.Warning, PythonCodes.IncludeMissing, $"{file}:{include.Line}: included file '{path}' does not exist.", location);
                continue;
            }

            if (chain.Contains(path.Value, StringComparer.Ordinal) || chain.Count >= MaxIncludeDepth)
            {
                result.Report(DiagnosticSeverity.Warning, PythonCodes.IncludeDepth,
                    $"{file}:{include.Line}: '{path}' is not read again (include cycle or more than {MaxIncludeDepth} levels).", location);
                continue;
            }

            if (await ReadAsync(context, path, result, cancellationToken) is { } included)
            {
                var nested = await ReadRequirementsAsync(context, path, included, existing, result, [.. chain, path.Value], cancellationToken);
                facts.Dependencies.AddRange(nested.Dependencies);
                facts.Problems.AddRange(nested.Problems);
            }
        }

        return facts;
    }

    private static async Task<string?> ReadAsync(ProjectSystemContext context, RelativePath path, FragmentBuilder result, CancellationToken cancellationToken)
    {
        var read = await context.FileReader.ReadAllTextAsync(context.Root, path, context.Limits.MaxManifestBytes, cancellationToken);
        if (read.TryGetValue(out var content, out var error))
        {
            return content;
        }

        result.Report(DiagnosticSeverity.Warning, PythonCodes.ManifestInvalid, $"{path}: could not be read: {error.Message} ({error.Code})", result.At(path, 1, 1));
        return null;
    }

    private static CodeProject CreateProject(ProjectSystemContext context, RelativePath folder, RelativePath[] manifests, List<PythonManifestFacts> facts)
    {
        var fallbackName = folder.IsRoot ? Path.GetFileName(Path.TrimEndingDirectorySeparator(context.Root.FullPath)) : folder.FileName;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PythonCodes.PropertyLayout] = HasSourceLayout(context, folder) ? "src" : "flat",
            [PythonCodes.PropertyManifests] = string.Join(';', manifests.Select(m => m.FileName).Order(StringComparer.Ordinal)),
        };

        Set(properties, PythonCodes.PropertyVersion, facts.Select(f => f.Version).FirstOrDefault(v => v is not null));
        Set(properties, PythonCodes.PropertyRequiresPython, facts.Select(f => f.RequiresPython).FirstOrDefault(v => v is not null));
        Set(properties, PythonCodes.PropertyBuildBackend, facts.Select(f => f.BuildBackend).FirstOrDefault(v => v is not null));
        if (manifests.Any(m => m.FileName == PythonCodes.SetupPy))
        {
            properties[PythonCodes.PropertySetupPy] = "true";
        }

        return CodeProject.Create(
            context.RepositoryId,
            facts.Select(f => f.Name).FirstOrDefault(n => n is not null) ?? fallbackName,
            ProjectKind.PythonProject,
            Ecosystem.Python,
            LanguageId.Python,
            manifests[0],
            folder,
            properties);
    }

    private static void Set(Dictionary<string, string> properties, string key, string? value)
    {
        if (value is not null)
        {
            properties[key] = value;
        }
    }

    /// <summary>A <c>src/</c> folder with Python files below the project folder (PyPA "src layout").</summary>
    private static bool HasSourceLayout(ProjectSystemContext context, RelativePath folder)
    {
        var source = folder.IsRoot ? RelativePath.Create("src") : RelativePath.Create($"{folder.Value}/src");
        return context.Files.Any(f => f.Language == LanguageId.Python && f.Path.IsWithin(source));
    }

    private static void AddDependencies(
        ProjectSystemContext context, FragmentBuilder result, CodeProject project, List<PythonDependency> dependencies, Dictionary<string, CodeProject> byName)
    {
        var source = new ElementReference(ElementKind.Project, project.Id);
        foreach (var dependency in dependencies)
        {
            var location = result.At(dependency.File, dependency.Line, dependency.Column);
            if (byName.TryGetValue(NormalizedName(dependency.Name), out var target))
            {
                if (target.Id != project.Id)
                {
                    result.AddRelationship(source, RelationshipKind.ProjectReference, new ElementReference(ElementKind.Project, target.Id),
                        EvidenceLevel.Confirmed, dependency.Rule, location, dependency.Attributes);
                }

                continue;
            }

            var external = ExternalDependency.Create(context.RepositoryId, PackageEcosystem.PyPI, dependency.Name);
            result.ExternalDependencies.Add(external);
            result.AddRelationship(source, RelationshipKind.PackageReference, new ElementReference(ElementKind.ExternalDependency, external.Id),
                EvidenceLevel.Confirmed, dependency.Rule, location, dependency.Attributes);
        }
    }

    /// <summary>PEP 503 normalization, as used for PyPI dependency names.</summary>
    private static string NormalizedName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.Trim(), "[-_.]+", "-").ToLowerInvariant();
}
