using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.ProjectSystems.DotNet;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.ProjectSystems.DotNet;

public sealed class ProjectFileParserTests
{
    private const long MaxCharacters = 1024 * 1024;
    private static readonly RepositoryId Repository = RepositoryId.New();

    private static Task<ProjectFileModel> ParseAsync(string projectPath, Dictionary<string, string> files) =>
        ProjectFileParser.ParseAsync(Repository, RelativePath.Create(projectPath), new InMemoryMsBuildFileSource(files),
            MaxCharacters, TestContext.Current.CancellationToken);

    private static async Task<ProjectFileModel> ParseFixtureAsync(string projectPath)
    {
        var root = new RepositoryRoot(TestRepositoryCatalog.GetFixturePath("dotnet-layered"));
        var reader = new RepositoryFileReader();
        var scan = await new RepositoryScanner(reader, TimeProvider.System)
            .ScanAsync(root, AnalysisLimits.Default, null, TestContext.Current.CancellationToken);
        var source = new RepositoryMsBuildFileSource(root, reader, scan.Files.Select(f => f.Path), MaxCharacters);

        return await ProjectFileParser.ParseAsync(Repository, RelativePath.Create(projectPath), source, MaxCharacters,
            TestContext.Current.CancellationToken);
    }

    // ----- Fixture dotnet-layered (EXPECTED.md §3–§6) -----

    [Fact]
    public async Task Fixture_Domain_PropertiesAndMultiTargeting()
    {
        var model = await ParseFixtureAsync("src/Shop.Domain/Shop.Domain.csproj");

        Assert.True(model.IsValid);
        Assert.Equal("Microsoft.NET.Sdk", model.Sdk);
        Assert.Equal(["net10.0", "net8.0"], model.TargetFrameworks);
        Assert.Equal("Shop.Domain", model.Properties["RootNamespace"]);
        Assert.Equal("enable", model.Properties["Nullable"]);
        Assert.Equal(["Directory.Build.props", "Directory.Packages.props"], model.ImportedFiles.Select(p => p.Value));
        Assert.Empty(model.ProjectReferences);
        Assert.Empty(model.Diagnostics);
    }

    [Fact]
    public async Task Fixture_Infrastructure_ReferencesAndFalseCondition()
    {
        var model = await ParseFixtureAsync("src/Shop.Infrastructure/Shop.Infrastructure.csproj");

        Assert.Equal(
            [
                ("src/Shop.Application/Shop.Application.csproj", 4),
                ("src/Shop.Domain/Shop.Domain.csproj", 5),
                ("src/Shop.Legacy/Shop.Legacy.csproj", 6),
            ],
            model.ProjectReferences.Select(r => (r.ResolvedPath!.Value.Value, r.Location.Line)));
        Assert.All(model.ProjectReferences, r => Assert.Equal(5, r.Location.Column));

        var package = Assert.Single(model.PackageReferences);
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", package.Name);
        Assert.Equal("10.0.15", package.VersionSpec);
        Assert.Equal(PackageVersionSource.Central, package.VersionSource);
        Assert.Equal("Directory.Packages.props:7:5", package.VersionLocation!.ToString());
        Assert.Equal("src/Shop.Infrastructure/Shop.Infrastructure.csproj:10:5", package.Location.ToString());
        Assert.Equal("false", model.Properties["UseSqlServer"]);
        Assert.Equal(["net10.0"], model.TargetFrameworks);
        Assert.Empty(model.Diagnostics);
    }

    [Fact]
    public async Task Fixture_Web_FrameworkReferenceAndProperties()
    {
        var model = await ParseFixtureAsync("src/Shop.Web/Shop.Web.csproj");

        var framework = Assert.Single(model.FrameworkReferences);
        Assert.Equal("Microsoft.AspNetCore.App", framework.Name);
        Assert.Equal("src/Shop.Web/Shop.Web.csproj:9:5", framework.Location.ToString());
        Assert.Equal("Exe", model.Properties["OutputType"]);
        Assert.Equal("ShopWeb", model.Properties["AssemblyName"]);
        Assert.Equal(
            ["src/Shop.Web/Shop.Web.csproj:13:5", "src/Shop.Web/Shop.Web.csproj:14:5"],
            model.ProjectReferences.Select(r => r.Location.ToString()));
    }

    [Fact]
    public async Task Fixture_Tests_CentralVersionsAndVersionOverride()
    {
        var model = await ParseFixtureAsync("tests/Shop.Tests/Shop.Tests.csproj");

        Assert.Equal(
            [
                ("xunit.v3", "4.0.3", PackageVersionSource.Central, "tests/Shop.Tests/Shop.Tests.csproj:8:5"),
                ("NSubstitute", "5.3.0", PackageVersionSource.Override, "tests/Shop.Tests/Shop.Tests.csproj:9:5"),
            ],
            model.PackageReferences.Select(p => (p.Name, p.VersionSpec, p.VersionSource, p.Location.ToString())));
        Assert.Equal(
            ["src/Shop.Domain/Shop.Domain.csproj", "src/Shop.Application/Shop.Application.csproj"],
            model.ProjectReferences.Select(r => r.ResolvedPath!.Value.Value));
        Assert.Equal("false", model.Properties["IsPackable"]);
    }

    // ----- Static evaluation rules -----

    [Fact]
    public async Task UnsupportedCondition_SkipsElementWithWarning()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["A/A.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup Condition="Exists('local.props')">
                    <PackageReference Include="Maybe" Version="1.0.0" />
                  </ItemGroup>
                  <ItemGroup>
                    <PackageReference Include="Always" Version="2.0.0" />
                  </ItemGroup>
                </Project>
                """,
        });

        Assert.Equal(["Always"], model.PackageReferences.Select(p => p.Name));
        var warning = Assert.Single(model.Diagnostics);
        Assert.Equal(DotNetCodes.UnsupportedCondition, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(2, warning.Location!.Value.StartLine);
    }

    [Fact]
    public async Task ConditionalProperty_UsesOnlyMatchingBranch()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["A/A.csproj"] = """
                <Project>
                  <PropertyGroup>
                    <Flavor>full</Flavor>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Flavor)' == 'full'">
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(Flavor)' == 'lite'">
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """,
        });

        Assert.Equal(["net10.0"], model.TargetFrameworks);
    }

    [Fact]
    public async Task ItemConditions_SeeFinalPropertyValues()
    {
        // MSBuild evaluates all properties before items, so a later property still affects an earlier item group.
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["A/A.csproj"] = """
                <Project>
                  <ItemGroup Condition="'$(UseExtra)' == 'true'">
                    <PackageReference Include="Extra" Version="1.0.0" />
                  </ItemGroup>
                  <PropertyGroup>
                    <UseExtra>true</UseExtra>
                  </PropertyGroup>
                </Project>
                """,
        });

        Assert.Equal(["Extra"], model.PackageReferences.Select(p => p.Name));
    }

    [Fact]
    public async Task PropertySubstitution_InItemPaths()
    {
        var model = await ParseAsync("src/A/A.csproj", new()
        {
            ["src/A/A.csproj"] = """
                <Project>
                  <PropertyGroup>
                    <SharedDir>..\Shared</SharedDir>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="$(SharedDir)\Shared.csproj" />
                  </ItemGroup>
                </Project>
                """,
        });

        Assert.Equal("src/Shared/Shared.csproj", Assert.Single(model.ProjectReferences).ResolvedPath!.Value.Value);
    }

    [Fact]
    public async Task UnknownPropertyInInclude_SkipsItemWithWarning()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["A/A.csproj"] = """
                <Project>
                  <ItemGroup>
                    <ProjectReference Include="$(RepoRoot)\B\B.csproj" />
                  </ItemGroup>
                </Project>
                """,
        });

        Assert.Empty(model.ProjectReferences);
        Assert.Equal(DotNetCodes.UnresolvedProperty, Assert.Single(model.Diagnostics).Code);
    }

    [Fact]
    public async Task PropertyFunction_MakesPropertyUnknownWithInfo()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["A/A.csproj"] = """
                <Project>
                  <PropertyGroup>
                    <Stamp>$([System.DateTime]::Now)</Stamp>
                  </PropertyGroup>
                </Project>
                """,
        });

        Assert.False(model.Properties.ContainsKey("Stamp"));
        var info = Assert.Single(model.Diagnostics);
        Assert.Equal(DotNetCodes.UnresolvedProperty, info.Code);
        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
    }

    [Fact]
    public async Task ImportElements_AreNotEvaluated()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["A/A.csproj"] = """
                <Project>
                  <Import Project="..\build\custom.targets" />
                </Project>
                """,
        });

        Assert.Equal(DotNetCodes.ImportNotEvaluated, Assert.Single(model.Diagnostics).Code);
    }

    [Fact]
    public async Task NearestDirectoryBuildProps_Wins()
    {
        var model = await ParseAsync("src/A/A.csproj", new()
        {
            ["Directory.Build.props"] = "<Project><PropertyGroup><Level>root</Level></PropertyGroup></Project>",
            ["src/Directory.Build.props"] = "<Project><PropertyGroup><Level>src</Level></PropertyGroup></Project>",
            ["src/A/A.csproj"] = "<Project />",
        });

        Assert.Equal("src", model.Properties["Level"]);
        Assert.Equal(["src/Directory.Build.props"], model.ImportedFiles.Select(p => p.Value));
    }

    [Fact]
    public async Task ProjectProperties_OverrideImportedOnes()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["Directory.Build.props"] = "<Project><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>",
            ["A/A.csproj"] = "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
        });

        Assert.Equal(["net10.0"], model.TargetFrameworks);
    }

    [Fact]
    public async Task VersionAsChildElement_AndWithoutCentralManagement()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["A/A.csproj"] = """
                <Project>
                  <ItemGroup>
                    <PackageReference Include="Child">
                      <Version>3.1.0</Version>
                    </PackageReference>
                    <PackageReference Include="Missing" />
                  </ItemGroup>
                </Project>
                """,
        });

        Assert.Equal(
            [("Child", "3.1.0", PackageVersionSource.Reference), ("Missing", (string?)null, PackageVersionSource.None)],
            model.PackageReferences.Select(p => (p.Name, p.VersionSpec, p.VersionSource)));
        Assert.Equal(DotNetCodes.MissingPackageVersion, Assert.Single(model.Diagnostics).Code);
    }

    [Fact]
    public async Task CentralManagementWithoutPackageVersion_ReportsMissingVersion()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["Directory.Packages.props"] = "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>",
            ["A/A.csproj"] = "<Project><ItemGroup><PackageReference Include=\"Unlisted\" /></ItemGroup></Project>",
        });

        Assert.Equal(PackageVersionSource.None, Assert.Single(model.PackageReferences).VersionSource);
        Assert.Equal(DotNetCodes.MissingPackageVersion, Assert.Single(model.Diagnostics).Code);
    }

    [Fact]
    public async Task LegacyMsBuildNamespace_IsSupported()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["A/A.csproj"] = """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\B\B.csproj" />
                  </ItemGroup>
                </Project>
                """,
        });

        Assert.Equal("v4.8", model.Properties["TargetFrameworkVersion"]);
        Assert.Equal("B/B.csproj", Assert.Single(model.ProjectReferences).ResolvedPath!.Value.Value);
    }

    [Fact]
    public async Task ReferenceOutsideRepository_IsKeptUnresolvedWithWarning()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["A/A.csproj"] = "<Project><ItemGroup><ProjectReference Include=\"..\\..\\outside\\X.csproj\" /></ItemGroup></Project>",
        });

        Assert.Null(Assert.Single(model.ProjectReferences).ResolvedPath);
        Assert.Equal(DotNetCodes.PathOutsideRepository, Assert.Single(model.Diagnostics).Code);
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE p [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;\">]><Project><PropertyGroup><X>&b;</X></PropertyGroup></Project>")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE p [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><Project><PropertyGroup><X>&x;</X></PropertyGroup></Project>")]
    [InlineData("<Project><PropertyGroup>")]
    [InlineData("<NotAProject />")]
    public async Task HostileOrInvalidXml_IsRejectedWithoutException(string content)
    {
        var model = await ParseAsync("A/A.csproj", new() { ["A/A.csproj"] = content });

        Assert.False(model.IsValid);
        Assert.Empty(model.Properties);
        Assert.Equal(DotNetCodes.InvalidXml, Assert.Single(model.Diagnostics).Code);
    }

    [Fact]
    public async Task InvalidDirectoryBuildProps_IsReportedAndProjectStillParsed()
    {
        var model = await ParseAsync("A/A.csproj", new()
        {
            ["Directory.Build.props"] = "<Project><broken",
            ["A/A.csproj"] = "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
        });

        Assert.True(model.IsValid);
        Assert.Equal(["net10.0"], model.TargetFrameworks);
        Assert.Empty(model.ImportedFiles);
        Assert.Equal(DotNetCodes.InvalidXml, Assert.Single(model.Diagnostics).Code);
    }

    [Fact]
    public void AnalysisModule_DoesNotReferenceMsBuild() =>
        Assert.DoesNotContain(
            typeof(ProjectFileParser).Assembly.GetReferencedAssemblies(),
            a => a.Name!.StartsWith("Microsoft.Build", StringComparison.Ordinal));
}
