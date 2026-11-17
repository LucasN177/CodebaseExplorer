using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;

namespace CodebaseExplorer.Analysis.Tests.Pipeline;

/// <summary>Test doubles for the analyzer contracts (TASK ANALYSIS-001: no fake production analyzers).</summary>
internal sealed class TestProjectSystemAnalyzer(
    string id,
    Func<ProjectSystemContext, AnalyzerFragment> analyze,
    Func<ScanResult, bool>? appliesTo = null) : IProjectSystemAnalyzer
{
    public AnalyzerDescriptor Descriptor { get; } = new(id, "1.0.0-test");

    public List<ProjectSystemContext> Calls { get; } = [];

    public bool AppliesTo(ScanResult scan) => appliesTo?.Invoke(scan) ?? true;

    public Task<AnalyzerFragment> AnalyzeAsync(ProjectSystemContext context, CancellationToken cancellationToken)
    {
        lock (Calls)
        {
            Calls.Add(context);
        }

        return Task.FromResult(analyze(context));
    }
}

internal sealed class TestLanguageAnalyzer(
    string id,
    string language,
    Func<LanguageAnalysisContext, AnalyzerFragment> analyze) : ILanguageAnalyzer
{
    public AnalyzerDescriptor Descriptor { get; } = new(id, "2.0.0-test");

    public List<LanguageAnalysisContext> Calls { get; } = [];

    public bool Supports(LanguageId languageId) => languageId.Value == language;

    public Task<AnalyzerFragment> AnalyzeProjectAsync(LanguageAnalysisContext context, CancellationToken cancellationToken)
    {
        lock (Calls)
        {
            Calls.Add(context);
        }

        return Task.FromResult(analyze(context));
    }
}

/// <summary>Builders for fragments produced by test analyzers.</summary>
internal static class Fragments
{
    public static CodeProject DotNetProject(RepositoryId repository, string manifestPath) =>
        CodeProject.Create(
            repository,
            Path.GetFileNameWithoutExtension(manifestPath),
            ProjectKind.DotNetProject,
            Ecosystem.DotNet,
            LanguageId.CSharp,
            RelativePath.Create(manifestPath),
            RelativePath.Create(Path.GetDirectoryName(manifestPath)!.Replace('\\', '/')));

    public static CodeRelationship ProjectReference(
        AnalysisRunId run, CodeProject source, CodeProject target, string analyzerId, int line, string manifestPath)
    {
        var sourceRef = new ElementReference(ElementKind.Project, source.Id);
        var targetRef = new ElementReference(ElementKind.Project, target.Id);
        var evidence = AnalysisEvidence.Create(
            CodeRelationship.IdFor(sourceRef, RelationshipKind.ProjectReference, targetRef),
            AnalyzerId.Create(analyzerId),
            "1",
            EvidenceLevel.Confirmed,
            "csproj.ProjectReference",
            SourceLocation.Line(RepositoryFile.IdFor(source.RepositoryId, RelativePath.Create(manifestPath)), line, 5, 60),
            run);

        return CodeRelationship.Create(sourceRef, RelationshipKind.ProjectReference, targetRef, [evidence]);
    }

    public static CodeModule Namespace(CodeProject project, string name) =>
        CodeModule.Create(project, ModuleKind.Namespace, name, name);
}
