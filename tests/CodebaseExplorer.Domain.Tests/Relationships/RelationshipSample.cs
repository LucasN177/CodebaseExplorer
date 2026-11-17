using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Tests.Projects;

namespace CodebaseExplorer.Domain.Tests.Relationships;

/// <summary>Builders for relationships and evidence used across tests.</summary>
internal static class RelationshipSample
{
    public static readonly AnalysisRunId Run = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000001"));
    public static readonly AnalyzerId MsBuild = AnalyzerId.Create("dotnet.msbuild-static");
    public static readonly AnalyzerId Roslyn = AnalyzerId.Create("csharp.roslyn");

    public static readonly ElementId ManifestFile =
        RepositoryFile.IdFor(Sample.Repository, RelativePath.Create("src/Billing/Billing.csproj"));

    public static ElementReference Project(string path) =>
        new(ElementKind.Project, ElementId.FromNaturalKey(NaturalKey.ForProject(Sample.Repository, "dotnet", path)));

    public static AnalysisEvidence EvidenceFor(
        ElementReference source,
        RelationshipKind kind,
        ElementReference target,
        int line = 12,
        EvidenceLevel level = EvidenceLevel.Confirmed,
        AnalyzerId? analyzer = null,
        string rule = "csproj.ProjectReference") =>
        AnalysisEvidence.Create(
            CodeRelationship.IdFor(source, kind, target),
            analyzer ?? MsBuild,
            "1.0.0",
            level,
            rule,
            SourceLocation.Line(ManifestFile, line, 5, 68),
            Run);

    public static CodeRelationship ProjectReference(ElementReference source, ElementReference target, params int[] lines) =>
        CodeRelationship.Create(
            source,
            RelationshipKind.ProjectReference,
            target,
            (lines.Length == 0 ? [12] : lines).Select(l => EvidenceFor(source, RelationshipKind.ProjectReference, target, l)));
}
