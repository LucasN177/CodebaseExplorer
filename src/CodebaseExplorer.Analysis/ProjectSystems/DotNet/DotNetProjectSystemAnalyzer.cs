using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>
/// Static .NET project-system analyzer (<c>dotnet.msbuild-static</c>, ANALYSIS_ENGINE.md Stage 2):
/// projects from <c>.csproj</c>/<c>.fsproj</c>/<c>.vbproj</c>, solution memberships from
/// <c>.sln</c>/<c>.slnx</c>, and <c>ProjectReference</c>, <c>PackageReference</c> and
/// <c>FrameworkReference</c> relationships with evidence. Never invokes MSBuild.
/// </summary>
internal sealed class DotNetProjectSystemAnalyzer : IProjectSystemAnalyzer
{
    public AnalyzerDescriptor Descriptor { get; } = new(DotNetCodes.AnalyzerName, "1.0.0");

    public bool AppliesTo(ScanResult scan) =>
        scan.Files.Any(f => f.Marker is ProjectMarker.DotNetProject or ProjectMarker.DotNetSolution or ProjectMarker.DotNetSolutionXml);

    public async Task<AnalyzerFragment> AnalyzeAsync(ProjectSystemContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var source = new RepositoryMsBuildFileSource(context.Root, context.FileReader, context.Files.Select(f => f.Path), context.Limits.MaxManifestBytes);
        var result = new FragmentBuilder(context.RepositoryId, context.AnalysisRunId, Descriptor);

        var memberships = await ParseSolutionsAsync(context, source, result, cancellationToken);

        var projectFiles = context.Files
            .Where(f => f.Marker == ProjectMarker.DotNetProject)
            .OrderBy(f => f.Path.Value, StringComparer.Ordinal)
            .ToArray();

        foreach (var file in projectFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var model = await ProjectFileParser.ParseAsync(
                context.RepositoryId, file.Path, source, context.Limits.MaxManifestBytes, cancellationToken);
            result.Diagnostics.AddRange(model.Diagnostics);

            var project = CreateProject(context.RepositoryId, file.Path, model, memberships[file.Path.Value]);
            result.Projects.Add(project);

            AddProjectReferences(result, project, model, source);
            AddExternalReferences(result, project, model);
        }

        return result.Build();
    }

    private static async Task<ILookup<string, SolutionMembership>> ParseSolutionsAsync(
        ProjectSystemContext context, RepositoryMsBuildFileSource source, FragmentBuilder result, CancellationToken cancellationToken)
    {
        var memberships = new List<(string ProjectPath, SolutionMembership Membership)>();
        var solutions = context.Files
            .Where(f => f.Marker is ProjectMarker.DotNetSolution or ProjectMarker.DotNetSolutionXml)
            .OrderBy(f => f.Path.Value, StringComparer.Ordinal);

        foreach (var solution in solutions)
        {
            var solutionFileId = RepositoryFile.IdFor(context.RepositoryId, solution.Path);
            var (content, error) = await source.LoadAsync(solution.Path, cancellationToken);
            if (content is null)
            {
                result.Diagnostics.Add(new AnalysisDiagnostic(DiagnosticSeverity.Warning, DotNetCodes.SolutionNotRecognized,
                    $"{solution.Path}: could not be read: {error}", DotNetCodes.Analyzer, SourceLocation.Line(solutionFileId, 1)));
                continue;
            }

            var document = SolutionParser.Parse(solution.Path, solutionFileId, content, context.Limits.MaxManifestBytes);
            result.Diagnostics.AddRange(document.Diagnostics);

            foreach (var entry in document.Projects)
            {
                if (!source.Exists(entry.ProjectPath))
                {
                    result.Diagnostics.Add(new AnalysisDiagnostic(DiagnosticSeverity.Warning, DotNetCodes.MissingProject,
                        $"{solution.Path}:{entry.Line}: listed project '{entry.ProjectPath}' does not exist.", DotNetCodes.Analyzer,
                        SourceLocation.Line(solutionFileId, entry.Line, entry.Column)));
                    continue;
                }

                memberships.Add((entry.ProjectPath.Value, new SolutionMembership(solutionFileId, entry.SolutionFolder)));
            }
        }

        return memberships.ToLookup(m => m.ProjectPath, m => m.Membership, StringComparer.Ordinal);
    }

    private static CodeProject CreateProject(
        RepositoryId repositoryId, RelativePath projectPath, ProjectFileModel model, IEnumerable<SolutionMembership> memberships)
    {
        var properties = model.Properties.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (model.Sdk is { } sdk)
        {
            properties["Sdk"] = sdk;
        }

        return CodeProject.Create(
            repositoryId,
            Path.GetFileNameWithoutExtension(projectPath.FileName),
            ProjectKind.DotNetProject,
            Ecosystem.DotNet,
            LanguageOf(projectPath),
            projectPath,
            RepositoryPaths.DirectoryOf(projectPath),
            properties,
            memberships);
    }

    private static void AddProjectReferences(FragmentBuilder result, CodeProject project, ProjectFileModel model, IMsBuildFileSource source)
    {
        foreach (var reference in model.ProjectReferences)
        {
            if (reference.ResolvedPath is not { } targetPath)
            {
                continue; // already reported by the parser (outside the repository or unknown values)
            }

            CodeProject target;
            EvidenceLevel level;
            if (source.Exists(targetPath))
            {
                target = ProjectIdentity(project.RepositoryId, targetPath);
                level = EvidenceLevel.Confirmed;
            }
            else
            {
                var directory = RepositoryPaths.DirectoryOf(targetPath);
                result.Diagnostics.Add(result.Warning(DotNetCodes.MissingProject,
                    $"{reference.Location}: referenced project file '{targetPath}' does not exist.", reference.Location));

                if (directory.IsRoot)
                {
                    continue; // a placeholder needs a directory to be identifiable
                }

                target = CodeProject.Create(
                    project.RepositoryId,
                    Path.GetFileNameWithoutExtension(targetPath.FileName),
                    ProjectKind.Unknown,
                    Ecosystem.DotNet,
                    LanguageOf(targetPath),
                    projectFilePath: null,
                    directory,
                    new Dictionary<string, string>
                    {
                        [ProjectProperties.Resolution] = ProjectProperties.ResolutionMissing,
                        [ProjectProperties.ReferencedPath] = targetPath.Value,
                    });
                result.Projects.Add(target);
                level = EvidenceLevel.Syntactic;
            }

            result.AddRelationship(
                new ElementReference(ElementKind.Project, project.Id),
                RelationshipKind.ProjectReference,
                new ElementReference(ElementKind.Project, target.Id),
                level,
                DotNetCodes.RuleProjectReference,
                reference.Location);
        }
    }

    private static void AddExternalReferences(FragmentBuilder result, CodeProject project, ProjectFileModel model)
    {
        var source = new ElementReference(ElementKind.Project, project.Id);

        foreach (var package in model.PackageReferences)
        {
            var dependency = ExternalDependency.Create(project.RepositoryId, PackageEcosystem.NuGet, package.Name);
            result.ExternalDependencies.Add(dependency);

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (package.VersionSpec is { } versionSpec)
            {
                attributes["versionSpec"] = versionSpec;
            }

            result.AddRelationship(source, RelationshipKind.PackageReference,
                new ElementReference(ElementKind.ExternalDependency, dependency.Id),
                EvidenceLevel.Confirmed, DotNetCodes.RulePackageReference, package.Location, attributes);
        }

        foreach (var framework in model.FrameworkReferences)
        {
            var dependency = ExternalDependency.Create(project.RepositoryId, PackageEcosystem.DotNetFramework, framework.Name);
            result.ExternalDependencies.Add(dependency);

            result.AddRelationship(source, RelationshipKind.FrameworkReference,
                new ElementReference(ElementKind.ExternalDependency, dependency.Id),
                EvidenceLevel.Confirmed, DotNetCodes.RuleFrameworkReference, framework.Location);
        }
    }

    /// <summary>
    /// The project a manifest path stands for. Only its id matters for references (ids derive from the
    /// manifest path), so properties are not needed here.
    /// </summary>
    private static CodeProject ProjectIdentity(RepositoryId repositoryId, RelativePath manifest) =>
        CodeProject.Create(repositoryId, Path.GetFileNameWithoutExtension(manifest.FileName), ProjectKind.DotNetProject,
            Ecosystem.DotNet, LanguageOf(manifest), manifest, RepositoryPaths.DirectoryOf(manifest));

    private static LanguageId LanguageOf(RelativePath projectPath) =>
        Path.GetExtension(projectPath.FileName).ToLowerInvariant() switch
        {
            ".fsproj" => LanguageId.Create("fsharp"),
            ".vbproj" => LanguageId.Create("vb"),
            _ => LanguageId.CSharp,
        };

    /// <summary>Collects fragment content and builds evidence-backed relationships.</summary>
    private sealed class FragmentBuilder(RepositoryId repositoryId, AnalysisRunId runId, AnalyzerDescriptor descriptor)
    {
        public List<CodeProject> Projects { get; } = [];

        public List<ExternalDependency> ExternalDependencies { get; } = [];

        public List<CodeRelationship> Relationships { get; } = [];

        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public void AddRelationship(
            ElementReference source,
            RelationshipKind kind,
            ElementReference target,
            EvidenceLevel level,
            string rule,
            MsBuildItemLocation location,
            IReadOnlyDictionary<string, string>? attributes = null)
        {
            var evidence = AnalysisEvidence.Create(
                CodeRelationship.IdFor(source, kind, target),
                descriptor.Id,
                descriptor.Version,
                level,
                rule,
                LocationOf(location),
                runId);

            Relationships.Add(CodeRelationship.Create(source, kind, target, [evidence], attributes));
        }

        public AnalysisDiagnostic Warning(string code, string message, MsBuildItemLocation location) =>
            new(DiagnosticSeverity.Warning, code, message, descriptor.Id, LocationOf(location));

        public AnalyzerFragment Build() => new()
        {
            Projects = Projects,
            ExternalDependencies = ExternalDependencies,
            Relationships = Relationships,
            Diagnostics = Diagnostics,
        };

        private SourceLocation LocationOf(MsBuildItemLocation location) =>
            SourceLocation.Line(RepositoryFile.IdFor(repositoryId, location.File), Math.Max(1, location.Line), Math.Max(1, location.Column));
    }
}
