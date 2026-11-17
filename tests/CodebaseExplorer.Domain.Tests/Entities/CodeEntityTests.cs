using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Tests.Projects;
using static CodebaseExplorer.Domain.Tests.Relationships.RelationshipSample;

namespace CodebaseExplorer.Domain.Tests.Entities;

public sealed class CodeEntityTests
{
    private static readonly SourceLocation Declaration = SourceLocation.Line(ManifestFile, 8, 1, 40);

    [Fact]
    public void Create_DerivesIdFromProjectLanguageAndSymbolKey()
    {
        var project = Sample.DotNetProject();
        var module = CodeModule.Create(project, ModuleKind.Namespace, "Invoices", "Billing.Invoices");

        var entity = CodeEntity.Create(project, module, EntityKind.Class, "InvoiceService", "Billing.Invoices.InvoiceService",
            LanguageId.CSharp, "T:Billing.Invoices.InvoiceService", [Declaration], Visibility.Public, EntityModifiers.Sealed);

        Assert.Equal(NaturalKey.ForEntity(project.NaturalKey, "csharp", "T:Billing.Invoices.InvoiceService"), entity.NaturalKey);
        Assert.Equal(module.Id, entity.ModuleId);
        Assert.Equal(project.Id, entity.ProjectId);
        Assert.Equal(EntityModifiers.Sealed, entity.Modifiers);
    }

    [Fact]
    public void Create_PartialType_KeepsAllDistinctDeclarations()
    {
        var project = Sample.DotNetProject();
        var module = CodeModule.Create(project, ModuleKind.Namespace, "Billing", "Billing");
        var second = SourceLocation.Line(ManifestFile, 20, 1, 30);

        var entity = CodeEntity.Create(project, module, EntityKind.Class, "Invoice", "Billing.Invoice", LanguageId.CSharp, "T:Billing.Invoice",
            [Declaration, second, Declaration], modifiers: EntityModifiers.Partial);

        Assert.Equal([Declaration, second], entity.Declarations);
    }

    [Fact]
    public void Create_WithoutDeclaration_Throws()
    {
        var project = Sample.DotNetProject();
        var module = CodeModule.Create(project, ModuleKind.Namespace, "Billing", "Billing");

        Assert.Throws<ArgumentException>(() =>
            CodeEntity.Create(project, module, EntityKind.Class, "A", "Billing.A", LanguageId.CSharp, "T:Billing.A", []));
    }

    [Fact]
    public void Create_ModuleOfOtherProject_Throws()
    {
        var module = CodeModule.Create(Sample.DotNetProject("A/A.csproj"), ModuleKind.Namespace, "A", "A");

        Assert.Throws<ArgumentException>(() => CodeEntity.Create(Sample.DotNetProject("B/B.csproj"), module, EntityKind.Class,
            "X", "A.X", LanguageId.CSharp, "T:A.X", [Declaration]));
    }

    [Fact]
    public void Create_Member_LinksContainingEntity()
    {
        var project = Sample.DotNetProject();
        var module = CodeModule.Create(project, ModuleKind.Namespace, "Billing", "Billing");
        var type = CodeEntity.Create(project, module, EntityKind.Class, "A", "Billing.A", LanguageId.CSharp, "T:Billing.A", [Declaration]);

        var method = CodeEntity.Create(project, module, EntityKind.Method, "Run", "Billing.A.Run", LanguageId.CSharp, "M:Billing.A.Run",
            [Declaration], Visibility.Public, containingEntity: type, signature: " void Run() ", metrics: new EntityMetrics(3, null));

        Assert.Equal(type.Id, method.ContainingEntityId);
        Assert.Equal("void Run()", method.Signature);
        Assert.Equal(3, method.Metrics!.LineCount);
    }

    [Fact]
    public void Create_SelfContainment_Throws()
    {
        var project = Sample.DotNetProject();
        var module = CodeModule.Create(project, ModuleKind.Namespace, "Billing", "Billing");
        var type = CodeEntity.Create(project, module, EntityKind.Class, "A", "Billing.A", LanguageId.CSharp, "T:Billing.A", [Declaration]);

        Assert.Throws<ArgumentException>(() => CodeEntity.Create(project, module, EntityKind.Class, "A", "Billing.A",
            LanguageId.CSharp, "T:Billing.A", [Declaration], containingEntity: type));
    }

    [Fact]
    public void Create_RejectsUndefinedEnumValues()
    {
        var project = Sample.DotNetProject();
        var module = CodeModule.Create(project, ModuleKind.Namespace, "Billing", "Billing");

        Assert.Throws<ArgumentOutOfRangeException>(() => CodeEntity.Create(project, module, (EntityKind)99, "A", "A",
            LanguageId.CSharp, "T:A", [Declaration]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CodeEntity.Create(project, module, EntityKind.Class, "A", "A",
            LanguageId.CSharp, "T:A", [Declaration], (Visibility)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => CodeEntity.Create(project, module, EntityKind.Class, "A", "A",
            LanguageId.CSharp, "T:A", [Declaration], modifiers: (EntityModifiers)1024));
    }

    [Fact]
    public void EntityMetrics_RejectNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityMetrics(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntityMetrics(0, -1));
    }
}
