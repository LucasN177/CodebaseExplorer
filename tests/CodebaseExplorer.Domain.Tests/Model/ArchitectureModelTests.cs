using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Tests.Projects;
using static CodebaseExplorer.Domain.Tests.Relationships.RelationshipSample;

namespace CodebaseExplorer.Domain.Tests.Model;

public sealed class ArchitectureModelTests
{
    private readonly ModelSample _sample = new();

    [Fact]
    public void ConsistentModel_HasNoViolations()
    {
        var model = _sample.Build();

        Assert.Empty(model.Validate());
        Assert.True(model.IsValid);
        model.EnsureValid();
    }

    [Fact]
    public void Lookups_FindElementsById()
    {
        var model = _sample.Build();

        Assert.Same(_sample.Billing, model.FindProject(_sample.Billing.Id));
        Assert.Same(_sample.InvoiceSource, model.FindFile(_sample.InvoiceSource.Id));
        Assert.Same(_sample.Namespace, model.FindModule(_sample.Namespace.Id));
        Assert.Same(_sample.Method, model.FindEntity(_sample.Method.Id));
        Assert.Same(_sample.Serilog, model.FindExternalDependency(_sample.Serilog.Id));
        Assert.Same(_sample.ProjectReference, model.FindRelationship(_sample.ProjectReference.Id));
        Assert.Null(model.FindProject(_sample.Namespace.Id));
    }

    [Fact]
    public void Contains_ChecksKindAndId()
    {
        var model = _sample.Build();

        Assert.True(model.Contains(new ElementReference(ElementKind.Project, _sample.Billing.Id)));
        Assert.False(model.Contains(new ElementReference(ElementKind.Module, _sample.Billing.Id)));
    }

    [Fact]
    public void Elements_AreOrderedByNaturalKey()
    {
        var model = _sample.Build(projects: [_sample.Shared, _sample.Billing]);

        Assert.Equal(
            model.Projects.Select(p => p.NaturalKey.Value).Order(StringComparer.Ordinal),
            model.Projects.Select(p => p.NaturalKey.Value));
    }

    [Fact]
    public void SameInputInDifferentOrder_ProducesSameSequence()
    {
        var first = _sample.Build();
        var second = _sample.Build(
            files: Enumerable.Reverse(_sample.Files),
            entities: Enumerable.Reverse(_sample.Entities),
            relationships: Enumerable.Reverse(_sample.Relationships));

        Assert.Equal(first.Files.Select(f => f.Id), second.Files.Select(f => f.Id));
        Assert.Equal(first.Entities.Select(e => e.Id), second.Entities.Select(e => e.Id));
        Assert.Equal(first.Relationships.Select(r => r.Id), second.Relationships.Select(r => r.Id));
    }

    [Fact]
    public void MissingRelationshipEndpoint_IsReported()
    {
        var model = _sample.Build(projects: [_sample.Billing]);

        var violation = Assert.Single(model.Validate(), v => v.Code == ModelViolationCode.MissingRelationshipEndpoint);
        Assert.Equal(_sample.ProjectReference.Id, violation.ElementId);
    }

    [Fact]
    public void MissingExternalDependency_IsReported()
    {
        var model = _sample.Build(dependencies: []);

        Assert.Contains(model.Validate(), v =>
            v.Code == ModelViolationCode.MissingRelationshipEndpoint && v.ElementId == _sample.PackageReference.Id);
    }

    [Fact]
    public void MissingModule_IsReported()
    {
        var model = _sample.Build(modules: []);

        Assert.Equal(2, model.Validate().Count(v => v.Code == ModelViolationCode.MissingModule));
    }

    [Fact]
    public void MissingContainingEntity_IsReported()
    {
        var model = _sample.Build(entities: [_sample.Method]);

        var violation = Assert.Single(model.Validate());
        Assert.Equal(ModelViolationCode.MissingContainingEntity, violation.Code);
        Assert.Equal(_sample.Method.Id, violation.ElementId);
    }

    [Fact]
    public void MissingFiles_AreReportedForManifestSolutionDeclarationAndEvidence()
    {
        var model = _sample.Build(files: []);
        var missingFiles = model.Validate().Where(v => v.Code == ModelViolationCode.MissingFile).ToArray();

        Assert.Contains(missingFiles, v => v.ElementId == _sample.Billing.Id && v.Message.Contains("manifest", StringComparison.Ordinal));
        Assert.Contains(missingFiles, v => v.ElementId == _sample.Billing.Id && v.Message.Contains("solution", StringComparison.Ordinal));
        Assert.Contains(missingFiles, v => v.ElementId == _sample.Service.Id);
        Assert.Contains(missingFiles, v => v.ElementId == _sample.ProjectReference.Evidence[0].Id);
    }

    [Fact]
    public void FileOfMissingProject_IsReported()
    {
        var model = _sample.Build(projects: [_sample.Shared], modules: [], entities: [], relationships: []);

        Assert.Contains(model.Validate(), v =>
            v.Code == ModelViolationCode.MissingProject && v.ElementId == _sample.InvoiceSource.Id);
    }

    [Fact]
    public void FileOutsideItsProjectRoot_IsReported()
    {
        var stray = ModelSample.File("docs/readme.md", FileClassification.Documentation, _sample.Billing.Id);

        var model = _sample.Build(files: [.. _sample.Files, stray]);

        var violation = Assert.Single(model.Validate());
        Assert.Equal(ModelViolationCode.FileOutsideProject, violation.Code);
    }

    [Fact]
    public void ElementOfOtherRepository_IsReported()
    {
        var foreign = ModelSample.File("other.cs", FileClassification.Source, repository: RepositoryId.New());

        var model = _sample.Build(files: [.. _sample.Files, foreign]);

        Assert.Equal(ModelViolationCode.WrongRepository, Assert.Single(model.Validate()).Code);
    }

    [Fact]
    public void DuplicateElement_IsReportedOnce()
    {
        var model = _sample.Build(relationships: [.. _sample.Relationships, _sample.ProjectReference]);

        var violation = Assert.Single(model.Validate());
        Assert.Equal(ModelViolationCode.DuplicateElement, violation.Code);
        Assert.Equal(_sample.ProjectReference.Id, violation.ElementId);
    }

    [Fact]
    public void ModuleParentCycle_IsReported()
    {
        var a1 = CodeModule.Create(_sample.Billing, ModuleKind.Namespace, "A", "A");
        var b = CodeModule.Create(_sample.Billing, ModuleKind.Namespace, "B", "A.B", a1);
        var a2 = CodeModule.Create(_sample.Billing, ModuleKind.Namespace, "A", "A", b); // same id as a1, parent b

        var model = _sample.Build(modules: [_sample.Namespace, a2, b]);

        var cycles = model.Validate().Where(v => v.Code == ModelViolationCode.ContainmentCycle).Select(v => v.ElementId).ToArray();
        Assert.Equal(new HashSet<ElementId> { a1.Id, b.Id }, cycles.ToHashSet());
    }

    [Fact]
    public void EntityContainmentCycle_IsReported()
    {
        var declaration = SourceLocation.Line(_sample.InvoiceSource.Id, 1, 1, 1);
        var outer1 = CodeEntity.Create(_sample.Billing, _sample.Namespace, EntityKind.Class, "O", "O", LanguageId.CSharp, "T:O", [declaration]);
        var inner = CodeEntity.Create(_sample.Billing, _sample.Namespace, EntityKind.Class, "I", "O.I", LanguageId.CSharp, "T:O.I", [declaration], containingEntity: outer1);
        var outer2 = CodeEntity.Create(_sample.Billing, _sample.Namespace, EntityKind.Class, "O", "O", LanguageId.CSharp, "T:O", [declaration], containingEntity: inner);

        var model = _sample.Build(entities: [.. _sample.Entities, outer2, inner]);

        Assert.Equal(2, model.Validate().Count(v => v.Code == ModelViolationCode.ContainmentCycle));
    }

    [Fact]
    public void Validate_ReportsAllViolationsNotOnlyTheFirst()
    {
        var model = _sample.Build(files: [], modules: [], dependencies: []);

        var codes = model.Validate().Select(v => v.Code).Distinct().ToArray();

        Assert.Contains(ModelViolationCode.MissingFile, codes);
        Assert.Contains(ModelViolationCode.MissingModule, codes);
        Assert.Contains(ModelViolationCode.MissingRelationshipEndpoint, codes);
    }

    [Fact]
    public void EnsureValid_ThrowsWithAllViolations()
    {
        var model = _sample.Build(files: []);

        var exception = Assert.Throws<ModelValidationException>(model.EnsureValid);

        Assert.Equal(model.Validate().Count, exception.Violations.Count);
        Assert.Contains("MissingFile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RejectsMissingIdsAndNullElements()
    {
        Assert.Throws<ArgumentException>(() => ArchitectureModel.Create(default, Run, [], [], [], [], [], []));
        Assert.Throws<ArgumentException>(() => ArchitectureModel.Create(Sample.Repository, default, [], [], [], [], [], []));
        Assert.Throws<ArgumentException>(() => ArchitectureModel.Create(Sample.Repository, Run, [null!], [], [], [], [], []));
    }

    [Fact]
    public void EmptyModel_IsValid() =>
        Assert.True(ArchitectureModel.Create(Sample.Repository, Run, [], [], [], [], [], []).IsValid);

    [Fact]
    public void RelationshipToEntityOfModel_IsResolved()
    {
        var source = new ElementReference(ElementKind.Entity, _sample.Method.Id);
        var target = new ElementReference(ElementKind.Entity, _sample.Service.Id);
        var call = ModelSample.Relationship(source, RelationshipKind.UsesType, target, _sample.InvoiceSource.Id, "csharp.semantic.TypeUsage");

        var model = _sample.Build(relationships: [.. _sample.Relationships, call]);

        Assert.Empty(model.Validate());
    }
}
