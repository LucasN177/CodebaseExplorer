using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;

using CodebaseExplorer.Domain.Tests.Projects;

namespace CodebaseExplorer.Domain.Tests.Modules;

public sealed class CodeModuleTests
{
    [Fact]
    public void Create_DerivesIdFromProjectKey()
    {
        var project = Sample.DotNetProject();

        var module = CodeModule.Create(project, ModuleKind.Namespace, "Invoices", "Billing.Invoices");

        Assert.Equal(NaturalKey.ForModule(project.NaturalKey, "namespace", "Billing.Invoices"), module.NaturalKey);
        Assert.Equal(project.Id, module.ProjectId);
        Assert.Equal(project.RepositoryId, module.RepositoryId);
        Assert.Null(module.ParentModuleId);
    }

    [Fact]
    public void Create_SameNamespaceInDifferentProjects_YieldsDifferentIds()
    {
        var first = CodeModule.Create(Sample.DotNetProject("A/A.csproj"), ModuleKind.Namespace, "Shared", "Shared");
        var second = CodeModule.Create(Sample.DotNetProject("B/B.csproj"), ModuleKind.Namespace, "Shared", "Shared");

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Create_WithParent_LinksParent()
    {
        var project = Sample.DotNetProject();
        var parent = CodeModule.Create(project, ModuleKind.Namespace, "Billing", "Billing");

        var child = CodeModule.Create(project, ModuleKind.Namespace, "Invoices", "Billing.Invoices", parent);

        Assert.Equal(parent.Id, child.ParentModuleId);
    }

    [Fact]
    public void Create_ParentFromOtherProject_Throws()
    {
        var parent = CodeModule.Create(Sample.DotNetProject("A/A.csproj"), ModuleKind.Namespace, "A", "A");

        Assert.Throws<ArgumentException>(() =>
            CodeModule.Create(Sample.DotNetProject("B/B.csproj"), ModuleKind.Namespace, "Sub", "A.Sub", parent));
    }

    [Fact]
    public void Create_SelfParent_Throws()
    {
        var project = Sample.DotNetProject();
        var module = CodeModule.Create(project, ModuleKind.Namespace, "Billing", "Billing");

        Assert.Throws<ArgumentException>(() => CodeModule.Create(project, ModuleKind.Namespace, "Billing", "Billing", module));
    }

    [Theory]
    [InlineData(ModuleKind.Namespace, "namespace")]
    [InlineData(ModuleKind.Directory, "directory")]
    [InlineData(ModuleKind.Package, "package")]
    [InlineData(ModuleKind.FileModule, "file")]
    public void ModuleKind_HasStableKeyToken(ModuleKind kind, string token) => Assert.Equal(token, kind.ToKeyToken());

    [Theory]
    [InlineData(Ecosystem.DotNet, "dotnet")]
    [InlineData(Ecosystem.Node, "node")]
    [InlineData(Ecosystem.Python, "python")]
    [InlineData(Ecosystem.Other, "other")]
    public void Ecosystem_HasStableKeyToken(Ecosystem ecosystem, string token) => Assert.Equal(token, ecosystem.ToKeyToken());
}
