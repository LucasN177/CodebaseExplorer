using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Dependencies;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Tests.Projects;
using static CodebaseExplorer.Domain.Tests.Relationships.RelationshipSample;

namespace CodebaseExplorer.Domain.Tests.Model;

/// <summary>
/// A small consistent model: projects Billing → Shared (ProjectReference), Billing → Serilog
/// (PackageReference), one namespace and one class with a method in Billing.
/// </summary>
internal sealed class ModelSample
{
    public ModelSample()
    {
        BillingManifest = File("src/Billing/Billing.csproj", FileClassification.ProjectManifest);
        SharedManifest = File("src/Shared/Shared.csproj", FileClassification.ProjectManifest);
        Solution = File("Shop.slnx", FileClassification.SolutionFile);

        Billing = CodeProject.Create(Sample.Repository, "Billing", ProjectKind.DotNetProject, Ecosystem.DotNet, LanguageId.CSharp,
            BillingManifest.Path, RelativePath.Create("src/Billing"), solutionMemberships: [new SolutionMembership(Solution.Id)]);
        Shared = CodeProject.Create(Sample.Repository, "Shared", ProjectKind.DotNetProject, Ecosystem.DotNet, LanguageId.CSharp,
            SharedManifest.Path, RelativePath.Create("src/Shared"));

        InvoiceSource = File("src/Billing/Invoices/InvoiceService.cs", FileClassification.Source, Billing.Id);

        Namespace = CodeModule.Create(Billing, ModuleKind.Namespace, "Invoices", "Billing.Invoices");
        Service = CodeEntity.Create(Billing, Namespace, EntityKind.Class, "InvoiceService", "Billing.Invoices.InvoiceService",
            LanguageId.CSharp, "T:Billing.Invoices.InvoiceService", [SourceLocation.Line(InvoiceSource.Id, 5, 1, 40)]);
        Method = CodeEntity.Create(Billing, Namespace, EntityKind.Method, "Create", "Billing.Invoices.InvoiceService.Create",
            LanguageId.CSharp, "M:Billing.Invoices.InvoiceService.Create", [SourceLocation.Line(InvoiceSource.Id, 9, 5, 30)],
            containingEntity: Service);

        Serilog = ExternalDependency.Create(Sample.Repository, PackageEcosystem.NuGet, "Serilog");

        ProjectReference = Relationship(
            new ElementReference(ElementKind.Project, Billing.Id), RelationshipKind.ProjectReference,
            new ElementReference(ElementKind.Project, Shared.Id), BillingManifest.Id, "csproj.ProjectReference");
        PackageReference = Relationship(
            new ElementReference(ElementKind.Project, Billing.Id), RelationshipKind.PackageReference,
            new ElementReference(ElementKind.ExternalDependency, Serilog.Id), BillingManifest.Id, "csproj.PackageReference");
    }

    public RepositoryFile BillingManifest { get; }

    public RepositoryFile SharedManifest { get; }

    public RepositoryFile Solution { get; }

    public RepositoryFile InvoiceSource { get; }

    public CodeProject Billing { get; }

    public CodeProject Shared { get; }

    public CodeModule Namespace { get; }

    public CodeEntity Service { get; }

    public CodeEntity Method { get; }

    public ExternalDependency Serilog { get; }

    public CodeRelationship ProjectReference { get; }

    public CodeRelationship PackageReference { get; }

    public List<RepositoryFile> Files => [BillingManifest, SharedManifest, Solution, InvoiceSource];

    public List<CodeProject> Projects => [Billing, Shared];

    public List<CodeModule> Modules => [Namespace];

    public List<CodeEntity> Entities => [Service, Method];

    public List<ExternalDependency> Dependencies => [Serilog];

    public List<CodeRelationship> Relationships => [ProjectReference, PackageReference];

    public ArchitectureModel Build(
        IEnumerable<RepositoryFile>? files = null,
        IEnumerable<CodeProject>? projects = null,
        IEnumerable<CodeModule>? modules = null,
        IEnumerable<CodeEntity>? entities = null,
        IEnumerable<ExternalDependency>? dependencies = null,
        IEnumerable<CodeRelationship>? relationships = null) =>
        ArchitectureModel.Create(
            Sample.Repository,
            Run,
            files ?? Files,
            projects ?? Projects,
            modules ?? Modules,
            entities ?? Entities,
            dependencies ?? Dependencies,
            relationships ?? Relationships);

    public static RepositoryFile File(string path, FileClassification classification, ElementId? projectId = null, RepositoryId? repository = null) =>
        RepositoryFile.Create(repository ?? Sample.Repository, RelativePath.Create(path), LanguageId.CSharp, classification, 100, Sample.Hash, projectId);

    public static CodeRelationship Relationship(
        ElementReference source, RelationshipKind kind, ElementReference target, ElementId evidenceFile, string rule) =>
        CodeRelationship.Create(source, kind, target,
        [
            AnalysisEvidence.Create(CodeRelationship.IdFor(source, kind, target), MsBuild, "1.0.0", EvidenceLevel.Confirmed,
                rule, SourceLocation.Line(evidenceFile, 3, 5, 60), Run),
        ]);
}
