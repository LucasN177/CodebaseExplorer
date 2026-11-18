using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Runs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using DiagnosticSeverity = CodebaseExplorer.Domain.Runs.DiagnosticSeverity;

namespace CodebaseExplorer.Analysis.Languages.CSharp;

/// <summary>
/// C# analyzer on Roslyn compiler APIs (<c>csharp.roslyn</c>, ANALYSIS_ENGINE.md §3, ADR-004): parses a
/// project's <c>.cs</c> files and creates a <see cref="CSharpCompilation"/> against the embedded .NET
/// reference assemblies and the compilations of its referenced in-repository projects (topological
/// order). Produces namespaces as modules and declared types and members as entities (ANALYSIS-005).
/// </summary>
/// <remarks>
/// Never uses MSBuild, never restores, never loads analyzers, source generators or any assembly from the
/// repository or from NuGet packages; source text is only parsed. All compilations are local to one run
/// and released when it ends. A failing project becomes an <c>Error</c> diagnostic; its dependents are
/// analyzed without its compilation.
/// </remarks>
internal sealed class CSharpAnalyzer : IRepositoryLanguageAnalyzer
{
    public AnalyzerDescriptor Descriptor { get; } = new(CSharpCodes.AnalyzerName, CSharpCodes.Version);

    public bool Supports(LanguageId language) => language == LanguageId.CSharp;

    /// <summary>Test hook: called with every compilation created, to verify that none outlives the run.</summary>
    internal Action<Compilation>? CompilationCreated { get; set; }

    public async Task<AnalyzerFragment> AnalyzeAsync(RepositoryLanguageContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<AnalysisDiagnostic>();
        var compilations = new Dictionary<ElementId, CSharpCompilation>();
        var modules = new List<Domain.Modules.CodeModule>();
        var entities = new List<Domain.Entities.CodeEntity>();
        var relationships = new List<Domain.Relationships.CodeRelationship>();

        // Entities by (assembly, documentation comment ID): the same full name may exist in several projects.
        var index = new Dictionary<(string Assembly, string Key), Domain.Entities.CodeEntity>();
        Domain.Entities.CodeEntity? EntityOf(ISymbol symbol) =>
            symbol.ContainingAssembly?.Name is { } assembly && symbol.GetDocumentationCommentId() is { } key
                ? index.GetValueOrDefault((assembly, key))
                : null;

        // Phase 1 (topological order): compile and collect declarations of every project, so that phase 2 can
        // resolve names against the entities of all projects (also projects without a declared reference).
        var compiled = new List<(ProjectInput Input, CSharpCompilation Compilation, Dictionary<SyntaxTree, SourceFile> Files)>();
        foreach (var input in TopologicalOrder(context, diagnostics))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var (compilation, files) = await CompileAsync(context, input, compilations, diagnostics, cancellationToken);
                compilations[input.Project.Id] = compilation;
                CompilationCreated?.Invoke(compilation);

                var declarations = new DeclarationCollector(input.Project, files);
                declarations.Collect(compilation, cancellationToken);
                modules.AddRange(declarations.Modules);
                entities.AddRange(declarations.Entities);
                foreach (var entity in declarations.Entities)
                {
                    index[(compilation.AssemblyName!, entity.SymbolKey)] = entity;
                }

                compiled.Add((input, compilation, files));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostics.Add(Failed(input, exception));
            }
        }

        // Phase 2: relationships (ANALYSIS-006).
        var types = entities.Where(e => e.SymbolKey.StartsWith("T:", StringComparison.Ordinal)).ToArray();
        foreach (var (input, compilation, files) in compiled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var references = new RelationshipCollector(EntityOf, types, files, context.AnalysisRunId);
                references.Collect(compilation, cancellationToken);
                relationships.AddRange(references.Relationships);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostics.Add(Failed(input, exception));
            }

            context.ProjectCompleted(input.Project.Name);
        }

        // Nothing of Roslyn outlives the run (ANALYSIS-006): only Domain objects leave this method.
        compiled.Clear();
        compilations.Clear();

        return new AnalyzerFragment
        {
            Modules = [.. modules.OrderBy(m => m.NaturalKey.Value, StringComparer.Ordinal)],
            Entities = [.. entities.OrderBy(e => e.NaturalKey.Value, StringComparer.Ordinal)],
            Relationships = [.. relationships.OrderBy(r => r.NaturalKey.Value, StringComparer.Ordinal)],
            Diagnostics = diagnostics,
        };
    }

    private static AnalysisDiagnostic Failed(ProjectInput input, Exception exception) =>
        new(DiagnosticSeverity.Error, CSharpCodes.ProjectFailed,
            $"C# analysis of project '{input.Project.Name}' failed: {exception.GetType().Name}: {exception.Message}", CSharpCodes.Analyzer);

    private static async Task<(CSharpCompilation Compilation, Dictionary<SyntaxTree, SourceFile> Files)> CompileAsync(
        RepositoryLanguageContext context,
        ProjectInput input,
        IReadOnlyDictionary<ElementId, CSharpCompilation> compiled,
        List<AnalysisDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var properties = input.Project.Properties;
        var parseOptions = CompilationSettings.ParseOptions(properties);
        var files = new Dictionary<SyntaxTree, SourceFile>();
        var trees = new List<SyntaxTree>();

        foreach (var file in input.Files.OrderBy(f => f.Path.Value, StringComparer.Ordinal))
        {
            var text = await context.FileReader.ReadAllTextAsync(context.Root, file.Path, context.Limits.MaxParseFileBytes, cancellationToken);
            if (!text.TryGetValue(out var source, out var error))
            {
                diagnostics.Add(new AnalysisDiagnostic(DiagnosticSeverity.Warning, CSharpCodes.FileNotRead,
                    $"{file.Path}: not analyzed ({error.Message}).", CSharpCodes.Analyzer));
                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(source, parseOptions, file.Path.Value, cancellationToken: cancellationToken);
            trees.Add(tree);
            files.Add(tree, new SourceFile(file.Path, RepositoryFile.IdFor(context.RepositoryId, file.Path), file.HasGeneratedHeader));
        }

        if (CompilationSettings.ImplicitUsingsSource(properties) is { } globalUsings)
        {
            // Not a repository file: part of the compilation, but never a source of entities or evidence.
            trees.Add(CSharpSyntaxTree.ParseText(globalUsings, parseOptions, "(implicit usings)", cancellationToken: cancellationToken));
        }

        var references = CompilationSettings.FrameworkReferences.Cast<MetadataReference>()
            .Concat(TransitiveReferences(input.Project.Id, context.ProjectReferences)
                .Where(compiled.ContainsKey)
                .Select(id => (MetadataReference)compiled[id].ToMetadataReference()));

        var compilation = CSharpCompilation.Create(
            AssemblyName(input.Project.Name), trees, references, CompilationSettings.CompilationOptions(properties));
        return (compilation, files);
    }

    /// <summary>Projects in dependency order (referenced before referencing); cycles are broken with a warning.</summary>
    private static List<ProjectInput> TopologicalOrder(RepositoryLanguageContext context, List<AnalysisDiagnostic> diagnostics)
    {
        var byId = context.Projects.ToDictionary(p => p.Project.Id);
        var order = new List<ProjectInput>();
        var state = new Dictionary<ElementId, bool>(); // false = visiting, true = done

        void Visit(ProjectInput input)
        {
            if (state.TryGetValue(input.Project.Id, out var done))
            {
                if (!done)
                {
                    diagnostics.Add(new AnalysisDiagnostic(DiagnosticSeverity.Warning, CSharpCodes.ReferenceCycle,
                        $"Project '{input.Project.Name}' is part of a project reference cycle; the cycle is broken for C# analysis.", CSharpCodes.Analyzer));
                }

                return;
            }

            state[input.Project.Id] = false;
            foreach (var reference in context.ProjectReferences.GetValueOrDefault(input.Project.Id, []))
            {
                if (byId.TryGetValue(reference, out var target))
                {
                    Visit(target);
                }
            }

            state[input.Project.Id] = true;
            order.Add(input);
        }

        foreach (var input in context.Projects)
        {
            Visit(input);
        }

        return order;
    }

    private static HashSet<ElementId> TransitiveReferences(ElementId project, IReadOnlyDictionary<ElementId, IReadOnlyList<ElementId>> references)
    {
        var result = new HashSet<ElementId>();
        var pending = new Stack<ElementId>(references.GetValueOrDefault(project, []));
        while (pending.TryPop(out var next))
        {
            if (next != project && result.Add(next))
            {
                foreach (var further in references.GetValueOrDefault(next, []))
                {
                    pending.Push(further);
                }
            }
        }

        return result;
    }

    private static string AssemblyName(string projectName) =>
        string.Concat(projectName.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_'));
}
