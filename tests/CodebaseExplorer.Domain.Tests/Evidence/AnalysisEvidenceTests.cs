using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Relationships;
using static CodebaseExplorer.Domain.Tests.Relationships.RelationshipSample;

namespace CodebaseExplorer.Domain.Tests.Evidence;

public sealed class AnalysisEvidenceTests
{
    private static readonly ElementId RelationshipId = CodeRelationship.IdFor(
        Project("a/A.csproj"), RelationshipKind.ProjectReference, Project("b/B.csproj"));

    [Fact]
    public void Create_DerivesIdFromRelationshipAnalyzerRuleAndLocation()
    {
        var location = SourceLocation.Line(ManifestFile, 12, 5, 68);

        var evidence = AnalysisEvidence.Create(RelationshipId, MsBuild, "1.0.0", EvidenceLevel.Confirmed, "csproj.ProjectReference", location, Run);

        Assert.Equal(
            NaturalKey.ForEvidence(RelationshipId, "dotnet.msbuild-static", "csproj.ProjectReference", ManifestFile, 12, 5),
            evidence.NaturalKey);
        Assert.Equal(ElementId.FromNaturalKey(evidence.NaturalKey), evidence.Id);
    }

    [Fact]
    public void Create_WithoutLocation_IsAllowed()
    {
        var evidence = AnalysisEvidence.Create(RelationshipId, AnalyzerId.Create("git.libgit2"), "1", EvidenceLevel.Heuristic, "git.cochange", null, Run);

        Assert.Null(evidence.Location);
        Assert.EndsWith(":-:0:0", evidence.NaturalKey.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_TruncatesLongExcerpts()
    {
        var evidence = AnalysisEvidence.Create(
            RelationshipId, MsBuild, "1", EvidenceLevel.Confirmed, "csproj.ProjectReference", null, Run, new string('x', 500));

        Assert.Equal(AnalysisEvidence.MaxExcerptLength, evidence.Excerpt!.Length);
    }

    [Fact]
    public void Create_BlankExcerpt_IsNull() =>
        Assert.Null(AnalysisEvidence.Create(RelationshipId, MsBuild, "1", EvidenceLevel.Confirmed, "r", null, Run, "  ").Excerpt);

    [Fact]
    public void Create_RejectsMissingParts()
    {
        Assert.Throws<ArgumentException>(() =>
            AnalysisEvidence.Create(default, MsBuild, "1", EvidenceLevel.Confirmed, "r", null, Run));
        Assert.Throws<ArgumentException>(() =>
            AnalysisEvidence.Create(RelationshipId, default, "1", EvidenceLevel.Confirmed, "r", null, Run));
        Assert.ThrowsAny<ArgumentException>(() =>
            AnalysisEvidence.Create(RelationshipId, MsBuild, " ", EvidenceLevel.Confirmed, "r", null, Run));
        Assert.Throws<ArgumentException>(() =>
            AnalysisEvidence.Create(RelationshipId, MsBuild, "1", EvidenceLevel.Confirmed, "r", null, default));
        Assert.ThrowsAny<ArgumentException>(() =>
            AnalysisEvidence.Create(RelationshipId, MsBuild, "1", EvidenceLevel.Confirmed, "bad rule", null, Run));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnalysisEvidence.Create(RelationshipId, MsBuild, "1", (EvidenceLevel)0, "r", null, Run));
    }

    [Fact]
    public void EvidenceLevels_AreOrdered()
    {
        Assert.True(EvidenceLevel.Heuristic < EvidenceLevel.Syntactic);
        Assert.True(EvidenceLevel.Syntactic < EvidenceLevel.Confirmed);
    }
}
