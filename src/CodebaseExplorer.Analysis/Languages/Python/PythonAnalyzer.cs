using System.Runtime.CompilerServices;
using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Languages.TreeSitter;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.Python;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;

namespace CodebaseExplorer.Analysis.Languages.Python;

/// <summary>
/// Tree-sitter analyzer for Python (<c>treesitter.python</c>, ANALYSIS_ENGINE.md Stage 3): folders with Python files
/// become <c>Package</c> modules and files <c>FileModule</c>s (dotted names relative to <c>src/</c> or the project root),
/// top-level classes and functions become entities, and <c>import</c>/<c>from … import</c> statements become
/// <c>Imports</c> to modules of the repository or to PyPI dependencies. Nothing is imported or executed.
/// </summary>
internal sealed class PythonAnalyzer : ILanguageAnalyzer
{
    public const string AnalyzerName = "treesitter.python";

    // Rules and diagnostics
    public const string RuleRelative = "py.import.relative";
    public const string RuleAbsolute = "py.import.absolute";
    public const string RulePackage = "py.import.package";
    public const string RulePackageMapped = "py.import.package-mapped";
    public const string RulePackageUndeclared = "py.import.package-undeclared";
    public const string ImportUnresolved = "py.import.unresolved";
    public const string FileNotRead = "py.file.not-read";
    public const string SyntaxErrors = "py.file.syntax-errors";

    private const string InitFile = "__init__.py";

    /// <summary>The module index is the same for every project of a run: built once per scanned file list.</summary>
    private static readonly ConditionalWeakTable<IReadOnlyList<ScannedFile>, ModuleIndex> Indexes = [];

    public AnalyzerDescriptor Descriptor { get; } = new(AnalyzerName, "1.0.0");

    public bool Supports(LanguageId language) => language == LanguageId.Python;

    public async Task<AnalyzerFragment> AnalyzeProjectAsync(LanguageAnalysisContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = new FragmentBuilder(context.RepositoryId, context.AnalysisRunId, Descriptor);
        var index = Indexes.GetValue(context.AllFiles, _ => new ModuleIndex(context.AllFiles, context.Projects));
        var declared = await DeclaredDistributionsAsync(context, cancellationToken);
        var packages = new Dictionary<string, CodeModule>(StringComparer.Ordinal);

        foreach (var file in context.ProjectFiles.OrderBy(f => f.Path.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = ModuleIndex.NameOf(context.Project, file.Path);
            var module = CreateModule(context.Project, name, packages, result);

            var read = await context.FileReader.ReadAllTextAsync(context.Root, file.Path, context.Limits.MaxParseFileBytes, cancellationToken);
            if (!read.TryGetValue(out var source, out var error))
            {
                result.Report(DiagnosticSeverity.Info, FileNotRead, $"{file.Path}: not parsed: {error.Message} ({error.Code})", result.At(file.Path, 1, 1));
                continue;
            }

            var facts = TreeSitterParser.ParseWithin(TreeSitterGrammar.Python, source, context.Limits.MaxParseDuration, PythonSyntax.Extract, cancellationToken);
            if (facts is null)
            {
                result.Report(DiagnosticSeverity.Warning, TreeSitterParser.ParseTimeoutCode,
                    $"{file.Path}: parsing took longer than {context.Limits.MaxParseDuration.TotalSeconds:0.#} s; the file's declarations and imports are skipped.", result.At(file.Path, 1, 1));
                continue;
            }

            if (facts.HasSyntaxErrors)
            {
                result.Report(DiagnosticSeverity.Info, SyntaxErrors, $"{file.Path}: contains syntax errors; declarations and imports may be incomplete.",
                    result.At(file.Path, 1, 1));
            }

            var fileId = RepositoryFile.IdFor(context.RepositoryId, file.Path);
            foreach (var group in facts.Declarations.GroupBy(d => d.Name, StringComparer.Ordinal))
            {
                var first = group.First();
                result.Entities.Add(CodeEntity.Create(
                    context.Project, module, first.Kind, first.Name, $"{module.QualifiedName}.{first.Name}", LanguageId.Python,
                    $"{module.QualifiedName}:{first.Name}", [Location(fileId, first.Span)],
                    first.Name.StartsWith('_') ? Visibility.Private : Visibility.Public, first.Modifiers));
            }

            foreach (var import in facts.Imports)
            {
                AddImport(context, result, index, declared, file.Path, name, module, import);
            }
        }

        return result.Build();
    }

    private static void AddImport(
        LanguageAnalysisContext context, FragmentBuilder result, ModuleIndex index, HashSet<string> declared,
        RelativePath path, ModuleName name, CodeModule source, PythonImport import)
    {
        var location = Location(RepositoryFile.IdFor(context.RepositoryId, path), import.Span);
        var from = new ElementReference(ElementKind.Module, source.Id);

        if (import.Level > 0)
        {
            var package = name.Package.Length == 0 ? [] : name.Package.Split('.');
            if (import.Level - 1 > package.Length)
            {
                result.Report(DiagnosticSeverity.Warning, ImportUnresolved,
                    $"{path}:{import.Span.Line}: relative import goes above the source root.", location);
                return;
            }

            var basePackage = string.Join('.', package.Take(package.Length - (import.Level - 1)));
            var target = Join(basePackage, import.Module);
            var resolved = (import.ImportedName is { } imported ? index.Find(Join(target, imported), context.Project) : null)
                ?? (target.Length > 0 ? index.Find(target, context.Project) : null);
            if (resolved is null)
            {
                result.Report(DiagnosticSeverity.Warning, ImportUnresolved,
                    $"{path}:{import.Span.Line}: relative import '{new string('.', import.Level)}{import.Module}' does not resolve to a module of the repository.", location);
                return;
            }

            AddModuleImport(result, from, resolved.Value, RuleRelative, location);
            return;
        }

        var absolute = (import.ImportedName is { } member ? index.Find(Join(import.Module, member), context.Project) : null)
            ?? index.FindLongestPrefix(import.Module, context.Project);
        if (absolute is not null)
        {
            AddModuleImport(result, from, absolute.Value, RuleAbsolute, location);
            return;
        }

        var top = import.Module.Split('.')[0];
        if (PythonNames.StandardLibrary.Contains(top))
        {
            return;
        }

        string distribution;
        string rule;
        EvidenceLevel level;
        var mapped = PythonNames.ImportToDistribution.GetValueOrDefault(import.Module.Split('.').Length > 1 ? string.Join('.', import.Module.Split('.').Take(2)) : top)
            ?? PythonNames.ImportToDistribution.GetValueOrDefault(top);
        if (declared.Contains(PythonNames.Normalize(top)))
        {
            (distribution, rule, level) = (top, RulePackage, EvidenceLevel.Syntactic);
        }
        else if (mapped is not null && declared.Contains(PythonNames.Normalize(mapped)))
        {
            (distribution, rule, level) = (mapped, RulePackageMapped, EvidenceLevel.Heuristic);
        }
        else
        {
            (distribution, rule, level) = (mapped ?? top, RulePackageUndeclared, EvidenceLevel.Heuristic);
        }

        var dependency = ExternalDependency.Create(context.RepositoryId, PackageEcosystem.PyPI, distribution);
        result.ExternalDependencies.Add(dependency);
        result.AddRelationship(from, RelationshipKind.Imports, new ElementReference(ElementKind.ExternalDependency, dependency.Id), level, rule, location);
    }

    private static void AddModuleImport(FragmentBuilder result, ElementReference from, ElementId target, string rule, SourceLocation location)
    {
        if (target != from.Id)
        {
            result.AddRelationship(from, RelationshipKind.Imports, new ElementReference(ElementKind.Module, target), EvidenceLevel.Confirmed, rule, location);
        }
    }

    private static string Join(string left, string right) => left.Length == 0 ? right : right.Length == 0 ? left : $"{left}.{right}";

    /// <summary>The file's module and the package modules above it (created once per project).</summary>
    private static CodeModule CreateModule(CodeProject project, ModuleName name, Dictionary<string, CodeModule> packages, FragmentBuilder result)
    {
        CodeModule? parent = null;
        var segments = name.Package.Length == 0 ? [] : name.Package.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            var qualified = string.Join('.', segments.Take(i + 1));
            if (!packages.TryGetValue(qualified, out var package))
            {
                package = CodeModule.Create(project, ModuleKind.Package, segments[i], qualified, parent);
                packages[qualified] = package;
                result.Modules.Add(package);
            }

            parent = package;
        }

        if (name.IsPackageInit && parent is not null)
        {
            return parent;
        }

        var module = CodeModule.Create(project, ModuleKind.FileModule, name.Qualified.Split('.')[^1], name.Qualified, parent);
        result.Modules.Add(module);
        return module;
    }

    /// <summary>Normalized names of the distributions declared by the manifests in the project's folder.</summary>
    private static async Task<HashSet<string>> DeclaredDistributionsAsync(LanguageAnalysisContext context, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var manifests = context.AllFiles
            .Where(f => RepositoryPaths.DirectoryOf(f.Path) == context.Project.RootDirectory
                && (f.Path.FileName is PythonCodes.PyProjectToml or PythonCodes.SetupCfg || f.Marker == ProjectMarker.PythonRequirements))
            .Select(f => f.Path);

        foreach (var manifest in manifests)
        {
            var read = await context.FileReader.ReadAllTextAsync(context.Root, manifest, context.Limits.MaxManifestBytes, cancellationToken);
            if (!read.TryGetValue(out var content, out _))
            {
                continue; // reported by the Python project-system analyzer
            }

            var facts = manifest.FileName switch
            {
                PythonCodes.PyProjectToml => PyProjectFile.Read(manifest, content),
                PythonCodes.SetupCfg => SetupCfgFile.Read(manifest, content),
                _ => RequirementsFile.Read(manifest, content, []),
            };
            names.UnionWith(facts.Dependencies.Select(d => PythonNames.Normalize(d.Name)));
        }

        return names;
    }

    private static SourceLocation Location(ElementId fileId, Span span) => new(fileId, span.Line, span.Column, span.EndLine, span.EndColumn);

    /// <summary>Dotted module name of a file: its package and whether it is the package's <c>__init__.py</c>.</summary>
    internal sealed record ModuleName(string Qualified, string Package, bool IsPackageInit);

    /// <summary>All Python modules of the repository by dotted name, with the project that owns them.</summary>
    private sealed class ModuleIndex
    {
        private readonly Dictionary<string, List<(CodeProject Owner, ModuleKind Kind)>> _modules = new(StringComparer.Ordinal);

        public ModuleIndex(IReadOnlyList<ScannedFile> files, IReadOnlyList<CodeProject> projects)
        {
            var owners = projects.OrderByDescending(p => p.RootDirectory.Value.Length).ToArray();
            foreach (var file in files.Where(f => f.Language == LanguageId.Python).OrderBy(f => f.Path.Value, StringComparer.Ordinal))
            {
                if (owners.FirstOrDefault(p => file.Path.IsWithin(p.RootDirectory)) is not { } owner)
                {
                    continue;
                }

                var name = NameOf(owner, file.Path);
                var segments = name.Package.Length == 0 ? [] : name.Package.Split('.');
                for (var i = 0; i < segments.Length; i++)
                {
                    Add(string.Join('.', segments.Take(i + 1)), owner, ModuleKind.Package);
                }

                if (!name.IsPackageInit || segments.Length == 0)
                {
                    Add(name.Qualified, owner, ModuleKind.FileModule);
                }
            }
        }

        /// <summary>Module id for <paramref name="qualified"/>, preferring the importing project and packages over files.</summary>
        public ElementId? Find(string qualified, CodeProject importer)
        {
            if (!_modules.TryGetValue(qualified, out var candidates))
            {
                return null;
            }

            var (owner, kind) = candidates
                .OrderBy(c => c.Owner.Id == importer.Id ? 0 : 1)
                .ThenBy(c => c.Kind == ModuleKind.Package ? 0 : 1)
                .First();
            return CodeModule.Create(owner, kind, qualified.Split('.')[^1], qualified).Id;
        }

        public ElementId? FindLongestPrefix(string qualified, CodeProject importer)
        {
            var segments = qualified.Split('.');
            for (var length = segments.Length; length > 0; length--)
            {
                if (Find(string.Join('.', segments.Take(length)), importer) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// The dotted name of <paramref name="file"/>: relative to <c>src/</c> for <c>layout=src</c> projects (files
        /// below it), otherwise to the project root; <c>__init__.py</c> names its package.
        /// </summary>
        public static ModuleName NameOf(CodeProject project, RelativePath file)
        {
            var root = project.RootDirectory;
            if (project.Properties.TryGetValue(PythonCodes.PropertyLayout, out var layout) && layout == "src")
            {
                var source = root.IsRoot ? RelativePath.Create("src") : RelativePath.Create($"{root.Value}/src");
                if (file.IsWithin(source))
                {
                    root = source;
                }
            }

            var relative = root.IsRoot ? file.Value : file.Value[(root.Value.Length + 1)..];
            var parts = relative.Split('/');
            var directories = parts[..^1];
            var package = string.Join('.', directories);
            if (parts[^1] == InitFile)
            {
                return directories.Length == 0 ? new ModuleName("__init__", string.Empty, false) : new ModuleName(package, package, true);
            }

            var stem = Path.GetFileNameWithoutExtension(parts[^1]);
            return new ModuleName(package.Length == 0 ? stem : $"{package}.{stem}", package, false);
        }

        private void Add(string qualified, CodeProject owner, ModuleKind kind)
        {
            if (!_modules.TryGetValue(qualified, out var list))
            {
                _modules[qualified] = list = [];
            }

            if (!list.Contains((owner, kind)))
            {
                list.Add((owner, kind));
            }
        }
    }
}
