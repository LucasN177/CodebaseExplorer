using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;

namespace CodebaseExplorer.Domain.Tests.Projects;

public sealed class CodeProjectTests
{
    [Fact]
    public void Create_UsesProjectFileAsIdentity()
    {
        var project = Sample.DotNetProject();

        Assert.Equal(NaturalKey.ForProject(Sample.Repository, "dotnet", "src/Billing/Billing.csproj"), project.NaturalKey);
        Assert.Equal(ElementId.FromNaturalKey(project.NaturalKey), project.Id);
        Assert.Equal(RepositoryFile.IdFor(Sample.Repository, RelativePath.Create("src/Billing/Billing.csproj")), project.ProjectFileId);
    }

    [Fact]
    public void Create_WithoutProjectFile_UsesRootDirectoryAsIdentity()
    {
        var project = CodeProject.Create(
            Sample.Repository, "billing", ProjectKind.PythonPackageFolder, Ecosystem.Python, LanguageId.Python,
            projectFilePath: null, RelativePath.Create("src/billing"));

        Assert.Equal(NaturalKey.ForProject(Sample.Repository, "python", "src/billing"), project.NaturalKey);
        Assert.Null(project.ProjectFileId);
    }

    [Fact]
    public void Create_AtRepositoryRootWithoutProjectFile_Throws() =>
        Assert.Throws<ArgumentException>(() => CodeProject.Create(
            Sample.Repository, "root", ProjectKind.PythonPackageFolder, Ecosystem.Python, LanguageId.Python,
            projectFilePath: null, RelativePath.Root));

    [Fact]
    public void Create_AtRepositoryRootWithProjectFile_IsAllowed()
    {
        var project = CodeProject.Create(
            Sample.Repository, "app", ProjectKind.NodePackage, Ecosystem.Node, LanguageId.TypeScript,
            RelativePath.Create("package.json"), RelativePath.Root);

        Assert.Equal(NaturalKey.ForProject(Sample.Repository, "node", "package.json"), project.NaturalKey);
    }

    [Fact]
    public void Create_ProjectFileOutsideRootDirectory_Throws() =>
        Assert.Throws<ArgumentException>(() => CodeProject.Create(
            Sample.Repository, "x", ProjectKind.DotNetProject, Ecosystem.DotNet, LanguageId.CSharp,
            RelativePath.Create("other/X.csproj"), RelativePath.Create("src/X")));

    [Fact]
    public void Create_CopiesAndSortsProperties()
    {
        var properties = new Dictionary<string, string> { ["TargetFramework"] = "net10.0", ["OutputType"] = "Exe" };

        var project = CodeProject.Create(
            Sample.Repository, "App", ProjectKind.DotNetProject, Ecosystem.DotNet, LanguageId.CSharp,
            RelativePath.Create("App/App.csproj"), RelativePath.Create("App"), properties);
        properties["TargetFramework"] = "changed";

        Assert.Equal(["OutputType", "TargetFramework"], project.Properties.Keys);
        Assert.Equal("net10.0", project.Properties["TargetFramework"]);
    }

    [Fact]
    public void Create_DeduplicatesSolutionMemberships()
    {
        var solution = RepositoryFile.IdFor(Sample.Repository, RelativePath.Create("Shop.slnx"));

        var project = CodeProject.Create(
            Sample.Repository, "App", ProjectKind.DotNetProject, Ecosystem.DotNet, LanguageId.CSharp,
            RelativePath.Create("App/App.csproj"), RelativePath.Create("App"),
            solutionMemberships: [new SolutionMembership(solution, "/src/"), new SolutionMembership(solution, "/src/")]);

        Assert.Single(project.SolutionMemberships);
    }

    [Fact]
    public void Create_RejectsBlankNameOrUndefinedKinds()
    {
        var file = RelativePath.Create("A/A.csproj");
        var root = RelativePath.Create("A");

        Assert.ThrowsAny<ArgumentException>(() =>
            CodeProject.Create(Sample.Repository, " ", ProjectKind.DotNetProject, Ecosystem.DotNet, LanguageId.CSharp, file, root));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CodeProject.Create(Sample.Repository, "A", (ProjectKind)42, Ecosystem.DotNet, LanguageId.CSharp, file, root));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CodeProject.Create(Sample.Repository, "A", ProjectKind.DotNetProject, (Ecosystem)42, LanguageId.CSharp, file, root));
    }
}
