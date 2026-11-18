using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Languages.TreeSitter;
using CodebaseExplorer.Analysis.Parsing;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.Node;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.Languages.TypeScript;

/// <summary>
/// Tree-sitter analyzer for TypeScript, TSX and JavaScript (<c>treesitter.typescript</c>, ANALYSIS_ENGINE.md Stage 3):
/// per project, folders become <c>Directory</c> modules and source files <c>FileModule</c>s; top-level declarations
/// become entities; <c>import</c>, <c>export … from</c>, <c>require(…)</c> and <c>import(…)</c> with literal
/// specifiers become <c>Imports</c> between file modules or to npm dependencies. Nothing is executed or type-checked.
/// </summary>
internal sealed class TypeScriptAnalyzer : ILanguageAnalyzer
{
    private static readonly HashSet<string> NodeBuiltins = new(StringComparer.Ordinal)
    {
        "assert", "async_hooks", "buffer", "child_process", "cluster", "console", "constants", "crypto", "dgram",
        "diagnostics_channel", "dns", "domain", "events", "fs", "http", "http2", "https", "inspector", "module", "net",
        "os", "path", "perf_hooks", "process", "punycode", "querystring", "readline", "repl", "stream", "string_decoder",
        "sys", "test", "timers", "tls", "trace_events", "tty", "url", "util", "v8", "vm", "wasi", "worker_threads", "zlib",
    };

    public AnalyzerDescriptor Descriptor { get; } = new(TypeScriptCodes.AnalyzerName, TypeScriptCodes.Version);

    public bool Supports(LanguageId language) => TreeSitterGrammars.For(language) is TreeSitterGrammar.TypeScript or TreeSitterGrammar.Tsx or TreeSitterGrammar.JavaScript;

    public async Task<AnalyzerFragment> AnalyzeProjectAsync(LanguageAnalysisContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var run = new ProjectRun(context, new FragmentBuilder(context.RepositoryId, context.AnalysisRunId, Descriptor));
        await run.PrepareAsync(cancellationToken);

        foreach (var file in context.ProjectFiles.OrderBy(f => f.Path.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await run.AnalyzeFileAsync(file.Path, file.Language, cancellationToken);
        }

        return run.Result.Build();
    }

    /// <summary>State of the analysis of one project.</summary>
    private sealed class ProjectRun(LanguageAnalysisContext context, FragmentBuilder result)
    {
        private readonly CodeProject _project = context.Project;
        private readonly ModuleFiles _files = new(context.AllFiles.Select(f => f.Path));
        private readonly Dictionary<string, CodeModule> _directories = new(StringComparer.Ordinal);
        private readonly HashSet<string> _declaredPackages = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CodeProject> _packages = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<string>> _entryPoints = new(StringComparer.Ordinal);

        /// <summary>Real projects, innermost root first (same ownership rule as the linker).</summary>
        private readonly CodeProject[] _owners = [.. context.Projects.OrderByDescending(p => p.RootDirectory.Value.Length)];

        private TsConfigResolution _tsconfig = TsConfigResolution.None;

        public FragmentBuilder Result { get; } = result;

        public async Task PrepareAsync(CancellationToken cancellationToken)
        {
            foreach (var project in context.Projects.Where(p => p.Ecosystem == Ecosystem.Node))
            {
                _packages.TryAdd(project.Name, project);
            }

            if (_project.Properties.TryGetValue(NodeCodes.PropertyTsConfig, out var tsconfig))
            {
                var (resolution, problems) = await TsConfigResolution.LoadAsync(RelativePath.Create(tsconfig), path => ReadManifestAsync(path, cancellationToken));
                _tsconfig = resolution;
                foreach (var problem in problems)
                {
                    Result.Report(DiagnosticSeverity.Warning, TypeScriptCodes.TsConfigNotRead, problem, Result.At(RelativePath.Create(tsconfig), 1, 1));
                }
            }

            // Declared packages: the project's own package.json and the workspace root's (hoisted dependencies).
            if (_project.Ecosystem == Ecosystem.Node)
            {
                await AddDeclaredAsync(_project, cancellationToken);
                if (_project.Properties.TryGetValue(NodeCodes.PropertyWorkspace, out var workspace) && _packages.TryGetValue(workspace, out var root))
                {
                    await AddDeclaredAsync(root, cancellationToken);
                }
            }
        }

        public async Task AnalyzeFileAsync(RelativePath path, LanguageId language, CancellationToken cancellationToken)
        {
            var relative = RelativeTo(_project, path);
            var directory = RepositoryPaths.DirectoryOf(relative);
            var module = CodeModule.Create(_project, ModuleKind.FileModule, relative.FileName, relative.Value, directory.IsRoot ? null : DirectoryModule(directory));
            Result.Modules.Add(module);

            var read = await context.FileReader.ReadAllTextAsync(context.Root, path, context.Limits.MaxParseFileBytes, cancellationToken);
            if (!read.TryGetValue(out var source, out var error))
            {
                Result.Report(DiagnosticSeverity.Info, TypeScriptCodes.FileNotRead, $"{path}: not parsed: {error.Message} ({error.Code})", Result.At(path, 1, 1));
                return;
            }

            var facts = TreeSitterParser.ParseWithin(
                TreeSitterGrammars.For(language)!.Value, source, context.Limits.MaxParseDuration, TypeScriptSyntax.Extract, cancellationToken);
            if (facts is null)
            {
                Result.Report(DiagnosticSeverity.Warning, TreeSitterParser.ParseTimeoutCode,
                    $"{path}: parsing took longer than {context.Limits.MaxParseDuration.TotalSeconds:0.#} s; the file's declarations and imports are skipped.", Result.At(path, 1, 1));
                return;
            }

            if (facts.HasSyntaxErrors)
            {
                Result.Report(DiagnosticSeverity.Info, TypeScriptCodes.SyntaxErrors,
                    $"{path}: contains syntax errors; declarations and imports may be incomplete.", Result.At(path, 1, 1));
            }

            AddEntities(path, relative, language, module, facts.Declarations);
            foreach (var reference in facts.References)
            {
                await AddReferenceAsync(path, module, reference, cancellationToken);
            }
        }

        private void AddEntities(RelativePath path, RelativePath relative, LanguageId language, CodeModule module, IReadOnlyList<Declaration> declarations)
        {
            var fileId = RepositoryFile.IdFor(context.RepositoryId, path);
            foreach (var group in declarations.GroupBy(d => SymbolKey(relative, d), StringComparer.Ordinal))
            {
                var first = group.First();
                Result.Entities.Add(CodeEntity.Create(
                    _project,
                    module,
                    first.Kind,
                    first.Name,
                    $"{relative.Value}#{first.Name}",
                    language,
                    group.Key,
                    group.Select(d => Location(fileId, d.Span)),
                    first.IsExported ? Visibility.Exported : Visibility.NotExported,
                    first.Modifiers));
            }
        }

        private static string SymbolKey(RelativePath relative, Declaration declaration) =>
            declaration.IsDefaultExport ? $"{relative.Value}#default"
            : declaration.IsExported ? $"{relative.Value}#{declaration.Name}"
            : $"{relative.Value}#~{declaration.Name}";

        private async Task AddReferenceAsync(RelativePath path, CodeModule source, ModuleReference reference, CancellationToken cancellationToken)
        {
            var location = Location(RepositoryFile.IdFor(context.RepositoryId, path), reference.Span);
            if (reference.Specifier is not { } specifier)
            {
                Result.Report(DiagnosticSeverity.Info, TypeScriptCodes.ImportComputed,
                    $"{path}:{reference.Span.Line}: {reference.Syntax} with a computed argument is not resolved.", location);
                return;
            }

            var (resolution, target, external) = await ResolveAsync(RepositoryPaths.DirectoryOf(path), specifier, cancellationToken);
            switch (resolution)
            {
                case null:
                    return; // built-in module or non-code file: nothing to show
                case TypeScriptCodes.ImportUnresolved:
                    Result.Report(DiagnosticSeverity.Warning, TypeScriptCodes.ImportUnresolved,
                        $"{path}:{reference.Span.Line}: '{specifier}' could not be resolved to a file of the repository.", location);
                    return;
                case TypeScriptCodes.ImportOutsideProjects:
                    Result.Report(DiagnosticSeverity.Info, TypeScriptCodes.ImportOutsideProjects,
                        $"{path}:{reference.Span.Line}: '{specifier}' resolves to '{target}', which belongs to no project.", location);
                    return;
            }

            var sourceReference = new ElementReference(ElementKind.Module, source.Id);
            var rule = TypeScriptCodes.Rule(reference.Syntax, resolution);
            if (external is { } package)
            {
                var dependency = ExternalDependency.Create(context.RepositoryId, PackageEcosystem.Npm, package);
                Result.ExternalDependencies.Add(dependency);
                var level = resolution == TypeScriptCodes.ResolutionPackage ? EvidenceLevel.Syntactic : EvidenceLevel.Heuristic;
                Result.AddRelationship(sourceReference, RelationshipKind.Imports, new ElementReference(ElementKind.ExternalDependency, dependency.Id), level, rule, location);
                return;
            }

            var targetModule = FileModuleOf(target!.Value);
            if (targetModule is null)
            {
                Result.Report(DiagnosticSeverity.Info, TypeScriptCodes.ImportOutsideProjects,
                    $"{path}:{reference.Span.Line}: '{specifier}' resolves to '{target}', which belongs to no project.", location);
                return;
            }

            if (targetModule.Value != source.Id)
            {
                Result.AddRelationship(sourceReference, RelationshipKind.Imports, new ElementReference(ElementKind.Module, targetModule.Value),
                    EvidenceLevel.Confirmed, rule, location);
            }
        }

        /// <summary>
        /// Resolution kind (a <c>TypeScriptCodes.Resolution*</c> value, an unresolved/outside code, or <see langword="null"/>
        /// for references that produce nothing), with the target file or npm package name.
        /// </summary>
        private async Task<(string? Resolution, RelativePath? File, string? Package)> ResolveAsync(RelativePath directory, string specifier, CancellationToken cancellationToken)
        {
            if (specifier.StartsWith('.'))
            {
                return RepositoryPaths.Resolve(directory, specifier) is { } candidate
                    ? FromFile(_files.Resolve(candidate), TypeScriptCodes.ResolutionRelative)
                    : (TypeScriptCodes.ImportUnresolved, null, null);
            }

            if (specifier.StartsWith('/') || specifier.StartsWith('#') || specifier.Contains(':', StringComparison.Ordinal) && !specifier.StartsWith("node:", StringComparison.Ordinal))
            {
                return (TypeScriptCodes.ImportUnresolved, null, null);
            }

            foreach (var candidate in _tsconfig.Substitute(specifier))
            {
                var found = _files.Resolve(candidate);
                if (found.Kind != FileResolution.Missing)
                {
                    return FromFile(found, TypeScriptCodes.ResolutionPathAlias);
                }
            }

            if (_tsconfig.BaseUrl is { } baseUrl && RepositoryPaths.Resolve(baseUrl, specifier) is { } fromBase
                && _files.Resolve(fromBase) is { Kind: not FileResolution.Missing } baseFound)
            {
                return FromFile(baseFound, TypeScriptCodes.ResolutionPathAlias);
            }

            var (package, subpath) = SplitPackage(specifier);
            if (specifier.StartsWith("node:", StringComparison.Ordinal) || NodeBuiltins.Contains(package))
            {
                return (null, null, null);
            }

            if (_packages.TryGetValue(package, out var workspacePackage))
            {
                return await ResolveWorkspaceAsync(workspacePackage, subpath, cancellationToken);
            }

            return (_declaredPackages.Contains(package) ? TypeScriptCodes.ResolutionPackage : TypeScriptCodes.ResolutionPackageUndeclared, null, package);
        }

        private async Task<(string? Resolution, RelativePath? File, string? Package)> ResolveWorkspaceAsync(CodeProject package, string subpath, CancellationToken cancellationToken)
        {
            var candidates = new List<RelativePath>();
            if (subpath.Length > 0)
            {
                candidates.AddRange(new[] { RepositoryPaths.Resolve(package.RootDirectory, subpath) }.OfType<RelativePath>());
            }
            else
            {
                candidates.AddRange((await EntryPointsAsync(package, cancellationToken)).Select(e => RepositoryPaths.Resolve(package.RootDirectory, e)).OfType<RelativePath>());
                candidates.Add(package.RootDirectory);
            }

            foreach (var candidate in candidates)
            {
                if (_files.Resolve(candidate) is { Kind: FileResolution.SourceFile, File: { } file })
                {
                    return (TypeScriptCodes.ResolutionWorkspace, file, null);
                }
            }

            return (TypeScriptCodes.ImportUnresolved, null, null);
        }

        private static (string? Resolution, RelativePath? File, string? Package) FromFile((FileResolution Kind, RelativePath? File) found, string resolution) =>
            found.Kind switch
            {
                FileResolution.SourceFile => (resolution, found.File, null),
                FileResolution.OtherFile => (null, null, null),
                _ => (TypeScriptCodes.ImportUnresolved, null, null),
            };

        /// <summary><c>lodash/fp</c> → (<c>lodash</c>, <c>fp</c>); <c>@scope/pkg/sub</c> → (<c>@scope/pkg</c>, <c>sub</c>).</summary>
        private static (string Package, string Subpath) SplitPackage(string specifier)
        {
            var segments = specifier.Split('/');
            var count = specifier.StartsWith('@') && segments.Length > 1 ? 2 : 1;
            return (string.Join('/', segments.Take(count)), string.Join('/', segments.Skip(count)));
        }

        /// <summary>Entry points of a workspace package, read once per project analysis and only when imported.</summary>
        private async Task<IReadOnlyList<string>> EntryPointsAsync(CodeProject package, CancellationToken cancellationToken)
        {
            if (!_entryPoints.TryGetValue(package.Name, out var known))
            {
                known = (await ReadPackageAsync(package, cancellationToken))?.EntryPoints ?? [];
                _entryPoints[package.Name] = known;
            }

            return known;
        }

        private async Task AddDeclaredAsync(CodeProject project, CancellationToken cancellationToken)
        {
            if (await ReadPackageAsync(project, cancellationToken) is { } manifest)
            {
                _declaredPackages.UnionWith(manifest.PackageDependencies.Select(d => d.Name));
            }
        }

        private async Task<PackageManifest?> ReadPackageAsync(CodeProject project, CancellationToken cancellationToken)
        {
            var path = project.RootDirectory.IsRoot ? RelativePath.Create(NodeCodes.PackageJson) : RelativePath.Create($"{project.RootDirectory.Value}/{NodeCodes.PackageJson}");
            var (content, _) = await ReadManifestAsync(path, cancellationToken);
            if (content is null || !SafeJson.TryParse(content, out var json, out _) || json is not JsonObject root)
            {
                return null; // reported by the Node project-system analyzer
            }

            var manifest = PackageManifest.From(root, []);
            _entryPoints[project.Name] = manifest.EntryPoints;
            return manifest;
        }

        private async Task<(string? Content, string? Error)> ReadManifestAsync(RelativePath path, CancellationToken cancellationToken)
        {
            var read = await context.FileReader.ReadAllTextAsync(context.Root, path, context.Limits.MaxManifestBytes, cancellationToken);
            return read.TryGetValue(out var content, out var error) ? (content, null) : (null, $"{error.Message} ({error.Code})");
        }

        private CodeModule DirectoryModule(RelativePath directory)
        {
            if (_directories.TryGetValue(directory.Value, out var existing))
            {
                return existing;
            }

            var parentPath = RepositoryPaths.DirectoryOf(directory);
            var parent = parentPath.IsRoot ? null : DirectoryModule(parentPath);
            var module = CodeModule.Create(_project, ModuleKind.Directory, directory.FileName, directory.Value, parent);
            _directories[directory.Value] = module;
            Result.Modules.Add(module);
            return module;
        }

        /// <summary>The id of the file module that the owning project's analysis creates for <paramref name="file"/>.</summary>
        private ElementId? FileModuleOf(RelativePath file)
        {
            var owner = _owners.FirstOrDefault(p => file.IsWithin(p.RootDirectory));
            if (owner is null)
            {
                return null;
            }

            var relative = RelativeTo(owner, file);
            return CodeModule.Create(owner, ModuleKind.FileModule, relative.FileName, relative.Value).Id;
        }

        private static RelativePath RelativeTo(CodeProject project, RelativePath path) =>
            project.RootDirectory.IsRoot ? path : RelativePath.Create(path.Value[(project.RootDirectory.Value.Length + 1)..]);

        private static SourceLocation Location(ElementId fileId, Span span) =>
            span.EndLine > span.Line || (span.EndLine == span.Line && span.EndColumn >= span.Column)
                ? new SourceLocation(fileId, span.Line, span.Column, span.EndLine, span.EndColumn)
                : SourceLocation.Line(fileId, span.Line, span.Column, span.Column);
    }
}
