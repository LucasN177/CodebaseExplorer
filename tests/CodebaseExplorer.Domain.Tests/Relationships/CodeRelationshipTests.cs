using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Relationships;
using static CodebaseExplorer.Domain.Tests.Relationships.RelationshipSample;

namespace CodebaseExplorer.Domain.Tests.Relationships;

public sealed class CodeRelationshipTests
{
    private static readonly ElementReference Billing = Project("src/Billing/Billing.csproj");
    private static readonly ElementReference Shared = Project("src/Shared/Shared.csproj");

    [Fact]
    public void Create_DerivesIdFromSourceKindTarget()
    {
        var relationship = ProjectReference(Billing, Shared);

        Assert.Equal(NaturalKey.ForRelationship(Billing.Id, "project-reference", Shared.Id), relationship.NaturalKey);
        Assert.Equal(CodeRelationship.IdFor(Billing, RelationshipKind.ProjectReference, Shared), relationship.Id);
    }

    [Fact]
    public void Create_WithoutEvidence_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            CodeRelationship.Create(Billing, RelationshipKind.ProjectReference, Shared, []));

    [Fact]
    public void Create_EvidenceOfOtherRelationship_Throws()
    {
        var foreign = EvidenceFor(Shared, RelationshipKind.ProjectReference, Billing);

        Assert.Throws<ArgumentException>(() =>
            CodeRelationship.Create(Billing, RelationshipKind.ProjectReference, Shared, [foreign]));
    }

    [Fact]
    public void Level_IsMaximumOfEvidenceLevels()
    {
        var source = new ElementReference(ElementKind.Entity, Billing.Id);
        var target = new ElementReference(ElementKind.Entity, Shared.Id);

        var relationship = CodeRelationship.Create(source, RelationshipKind.UsesType, target,
        [
            EvidenceFor(source, RelationshipKind.UsesType, target, 3, EvidenceLevel.Heuristic, rule: "heuristic.a"),
            EvidenceFor(source, RelationshipKind.UsesType, target, 4, EvidenceLevel.Syntactic, rule: "syntax.b"),
        ]);

        Assert.Equal(EvidenceLevel.Syntactic, relationship.Level);
    }

    [Fact]
    public void Occurrences_CountsDistinctEvidence()
    {
        var evidence = EvidenceFor(Billing, RelationshipKind.ProjectReference, Shared, 12);

        var relationship = CodeRelationship.Create(Billing, RelationshipKind.ProjectReference, Shared,
            [evidence, evidence, EvidenceFor(Billing, RelationshipKind.ProjectReference, Shared, 13)]);

        Assert.Equal(2, relationship.Occurrences);
        Assert.Equal(2, relationship.Evidence.Count);
    }

    [Fact]
    public void Origin_DefaultsToFirstEvidenceAnalyzer()
    {
        var relationship = ProjectReference(Billing, Shared);

        Assert.Equal(MsBuild, relationship.Origin);
    }

    [Fact]
    public void Origin_MustHaveProducedEvidence() =>
        Assert.Throws<ArgumentException>(() => CodeRelationship.Create(
            Billing, RelationshipKind.ProjectReference, Shared,
            [EvidenceFor(Billing, RelationshipKind.ProjectReference, Shared)],
            origin: Roslyn));

    [Theory]
    [InlineData(RelationshipKind.ProjectReference, ElementKind.Project, ElementKind.Module)]
    [InlineData(RelationshipKind.PackageReference, ElementKind.Project, ElementKind.Project)]
    [InlineData(RelationshipKind.FrameworkReference, ElementKind.Entity, ElementKind.ExternalDependency)]
    [InlineData(RelationshipKind.Inherits, ElementKind.Entity, ElementKind.Module)]
    [InlineData(RelationshipKind.Calls, ElementKind.File, ElementKind.Entity)]
    [InlineData(RelationshipKind.Imports, ElementKind.Project, ElementKind.File)]
    [InlineData(RelationshipKind.Imports, ElementKind.ExternalDependency, ElementKind.File)]
    public void Create_InvalidEndpointKinds_Throw(RelationshipKind kind, ElementKind sourceKind, ElementKind targetKind)
    {
        var source = new ElementReference(sourceKind, Billing.Id);
        var target = new ElementReference(targetKind, Shared.Id);

        Assert.Throws<ArgumentException>(() => CodeRelationship.Create(source, kind, target, []));
    }

    [Theory]
    [InlineData(RelationshipKind.Imports, ElementKind.File, ElementKind.ExternalDependency)]
    [InlineData(RelationshipKind.Imports, ElementKind.Module, ElementKind.Module)]
    [InlineData(RelationshipKind.PackageReference, ElementKind.Project, ElementKind.ExternalDependency)]
    [InlineData(RelationshipKind.DependsOn, ElementKind.Module, ElementKind.Project)]
    [InlineData(RelationshipKind.RegistersService, ElementKind.Entity, ElementKind.Entity)]
    public void Create_ValidEndpointKinds_Succeed(RelationshipKind kind, ElementKind sourceKind, ElementKind targetKind)
    {
        var source = new ElementReference(sourceKind, Billing.Id);
        var target = new ElementReference(targetKind, Shared.Id);

        var relationship = CodeRelationship.Create(source, kind, target, [EvidenceFor(source, kind, target)]);

        Assert.Equal(kind, relationship.Kind);
    }

    [Fact]
    public void Create_SelfReference_OnlyAllowedForCalls()
    {
        var entity = new ElementReference(ElementKind.Entity, Billing.Id);

        var recursion = CodeRelationship.Create(entity, RelationshipKind.Calls, entity,
            [EvidenceFor(entity, RelationshipKind.Calls, entity, rule: "csharp.semantic.Invocation")]);

        Assert.Equal(entity, recursion.Target);
        Assert.Throws<ArgumentException>(() => CodeRelationship.Create(Billing, RelationshipKind.ProjectReference, Billing, []));
    }

    [Fact]
    public void Attributes_AreCopiedAndSorted()
    {
        var dependency = new ElementReference(ElementKind.ExternalDependency, Shared.Id);
        var attributes = new Dictionary<string, string> { ["versionSpec"] = "[4.0,)", ["devOnly"] = "false" };

        var relationship = CodeRelationship.Create(Billing, RelationshipKind.PackageReference, dependency,
            [EvidenceFor(Billing, RelationshipKind.PackageReference, dependency, rule: "csproj.PackageReference")], attributes);
        attributes["versionSpec"] = "changed";

        Assert.Equal(["devOnly", "versionSpec"], relationship.Attributes.Keys);
        Assert.Equal("[4.0,)", relationship.Attributes["versionSpec"]);
    }

    [Fact]
    public void WithEvidence_MergesEvidenceAndKeepsExistingAttributes()
    {
        var dependency = new ElementReference(ElementKind.ExternalDependency, Shared.Id);
        var relationship = CodeRelationship.Create(Billing, RelationshipKind.PackageReference, dependency,
            [EvidenceFor(Billing, RelationshipKind.PackageReference, dependency, 10, EvidenceLevel.Syntactic, rule: "csproj.PackageReference")],
            new Dictionary<string, string> { ["versionSpec"] = "1.0" });

        var merged = relationship.WithEvidence(
            [EvidenceFor(Billing, RelationshipKind.PackageReference, dependency, 11, EvidenceLevel.Confirmed, rule: "csproj.PackageReference")],
            new Dictionary<string, string> { ["versionSpec"] = "2.0", ["devOnly"] = "true" });

        Assert.Equal(2, merged.Occurrences);
        Assert.Equal(EvidenceLevel.Confirmed, merged.Level);
        Assert.Equal("1.0", merged.Attributes["versionSpec"]);
        Assert.Equal("true", merged.Attributes["devOnly"]);
        Assert.Equal(1, relationship.Occurrences);
        Assert.Equal(relationship.Id, merged.Id);
    }

    [Fact]
    public void WithEvidence_ForeignEvidence_Throws()
    {
        var relationship = ProjectReference(Billing, Shared);

        Assert.Throws<ArgumentException>(() =>
            relationship.WithEvidence([EvidenceFor(Shared, RelationshipKind.ProjectReference, Billing)]));
    }

    [Theory]
    [InlineData(RelationshipKind.ProjectReference, "project-reference")]
    [InlineData(RelationshipKind.PackageReference, "package-reference")]
    [InlineData(RelationshipKind.FrameworkReference, "framework-reference")]
    [InlineData(RelationshipKind.Imports, "imports")]
    [InlineData(RelationshipKind.Inherits, "inherits")]
    [InlineData(RelationshipKind.Implements, "implements")]
    [InlineData(RelationshipKind.UsesType, "uses-type")]
    [InlineData(RelationshipKind.Instantiates, "instantiates")]
    [InlineData(RelationshipKind.Calls, "calls")]
    [InlineData(RelationshipKind.RegistersService, "registers-service")]
    [InlineData(RelationshipKind.DependsOn, "depends-on")]
    public void RelationshipKind_HasStableKeyToken(RelationshipKind kind, string token) =>
        Assert.Equal(token, kind.ToKeyToken());

    [Fact]
    public void EveryRelationshipKind_HasKeyToken() =>
        Assert.All(Enum.GetValues<RelationshipKind>(), kind => Assert.False(string.IsNullOrEmpty(kind.ToKeyToken())));
}
