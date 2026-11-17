using CodebaseExplorer.Analysis.ProjectSystems.DotNet;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.ProjectSystems.DotNet;

public sealed class SolutionParserTests
{
    private const long MaxCharacters = 1024 * 1024;
    private static readonly ElementId FileId = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("solution-file"));

    private static SolutionDocument ParseFixture(string fileName)
    {
        var content = File.ReadAllText(Path.Combine(TestRepositoryCatalog.GetFixturePath("dotnet-layered"), fileName));
        return SolutionParser.Parse(RelativePath.Create(fileName), FileId, content, MaxCharacters);
    }

    [Fact]
    public void Fixture_SlnAndSlnx_ProduceIdenticalMemberships()
    {
        var sln = ParseFixture("Shop.sln");
        var slnx = ParseFixture("Shop.slnx");

        var expected = new (string, string?)[]
        {
            ("src/Shop.Domain/Shop.Domain.csproj", "/src/"),
            ("src/Shop.Application/Shop.Application.csproj", "/src/"),
            ("src/Shop.Infrastructure/Shop.Infrastructure.csproj", "/src/"),
            ("src/Shop.Web/Shop.Web.csproj", "/src/"),
            ("tests/Shop.Tests/Shop.Tests.csproj", "/tests/"),
        };

        Assert.Equal(expected, sln.Projects.Select(p => (p.ProjectPath.Value, p.SolutionFolder)));
        Assert.Equal(expected, slnx.Projects.Select(p => (p.ProjectPath.Value, p.SolutionFolder)));
        Assert.Empty(sln.Diagnostics);
        Assert.Empty(slnx.Diagnostics);
    }

    [Fact]
    public void Fixture_EntryPositionsMatchExpectedMd()
    {
        Assert.Equal([(10, 1), (12, 1), (14, 1), (16, 1), (18, 1)], ParseFixture("Shop.sln").Projects.Select(p => (p.Line, p.Column)));
        Assert.Equal([(3, 5), (4, 5), (5, 5), (6, 5), (9, 5)], ParseFixture("Shop.slnx").Projects.Select(p => (p.Line, p.Column)));
    }

    [Fact]
    public void Sln_NestedFolders_BuildFullFolderPath()
    {
        const string content = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{A0000000-0000-0000-0000-000000000001}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "core", "core", "{A0000000-0000-0000-0000-000000000002}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Lib", "src\core\Lib\Lib.csproj", "{B0000000-0000-0000-0000-000000000001}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Top", "Top\Top.csproj", "{B0000000-0000-0000-0000-000000000002}"
            EndProject
            Global
            	GlobalSection(NestedProjects) = preSolution
            		{A0000000-0000-0000-0000-000000000002} = {A0000000-0000-0000-0000-000000000001}
            		{B0000000-0000-0000-0000-000000000001} = {A0000000-0000-0000-0000-000000000002}
            	EndGlobalSection
            EndGlobal
            """;

        var document = SolutionParser.ParseSln(RelativePath.Create("All.sln"), FileId, content);

        Assert.Equal(
            [("src/core/Lib/Lib.csproj", "/src/core/"), ("Top/Top.csproj", (string?)null)],
            document.Projects.Select(p => (p.ProjectPath.Value, p.SolutionFolder)));
    }

    [Fact]
    public void Sln_SolutionInSubdirectory_ResolvesRelativeAndParentPaths()
    {
        const string content = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "A", "A\A.csproj", "{B0000000-0000-0000-0000-000000000001}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Shared", "..\shared\Shared.csproj", "{B0000000-0000-0000-0000-000000000002}"
            EndProject
            """;

        var document = SolutionParser.ParseSln(RelativePath.Create("apps/App.sln"), FileId, content);

        Assert.Equal(["apps/A/A.csproj", "shared/Shared.csproj"], document.Projects.Select(p => p.ProjectPath.Value));
    }

    [Fact]
    public void Sln_PathOutsideRepository_IsReportedNotFollowed()
    {
        const string content = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Evil", "..\..\outside\Evil.csproj", "{B0000000-0000-0000-0000-000000000001}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Rooted", "C:\abs\Rooted.csproj", "{B0000000-0000-0000-0000-000000000002}"
            EndProject
            """;

        var document = SolutionParser.ParseSln(RelativePath.Create("apps/App.sln"), FileId, content);

        Assert.Empty(document.Projects);
        Assert.Equal(2, document.Diagnostics.Count(d => d.Code == DotNetCodes.PathOutsideRepository));
    }

    [Fact]
    public void Sln_NonProjectEntries_AreSkipped()
    {
        const string content = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{E24C65DC-7377-472B-9ABA-BC803B73C61A}") = "Site", "http://localhost/site", "{B0000000-0000-0000-0000-000000000001}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Solution Items", "Solution Items", "{A0000000-0000-0000-0000-000000000001}"
            EndProject
            """;

        var document = SolutionParser.ParseSln(RelativePath.Create("X.sln"), FileId, content);

        Assert.Empty(document.Projects);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Sln_MalformedEntry_YieldsWarningAndKeepsValidEntries()
    {
        const string content = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{broken") = "A"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "B", "B\B.csproj", "{B0000000-0000-0000-0000-000000000002}"
            EndProject
            """;

        var document = SolutionParser.ParseSln(RelativePath.Create("X.sln"), FileId, content);

        Assert.Equal(["B/B.csproj"], document.Projects.Select(p => p.ProjectPath.Value));
        var warning = Assert.Single(document.Diagnostics);
        Assert.Equal(DotNetCodes.SolutionMalformedEntry, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(2, warning.Location!.Value.StartLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("just some text")]
    [InlineData("<Solution />")]
    public void Sln_WithoutHeader_IsNotRecognized(string content)
    {
        var document = SolutionParser.ParseSln(RelativePath.Create("X.sln"), FileId, content);

        Assert.Empty(document.Projects);
        Assert.Equal(DotNetCodes.SolutionNotRecognized, Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Slnx_TopLevelProjectAndNestedFolderNames()
    {
        const string content = """
            <Solution>
              <Project Path="Top/Top.csproj" />
              <Folder Name="/src/core/">
                <Project Path="src/core/Lib/Lib.csproj" />
              </Folder>
              <Folder Name="/docs/">
                <File Path="README.md" />
              </Folder>
            </Solution>
            """;

        var document = SolutionParser.ParseSlnx(RelativePath.Create("All.slnx"), FileId, content, MaxCharacters);

        Assert.Equal(
            [("Top/Top.csproj", (string?)null), ("src/core/Lib/Lib.csproj", "/src/core/")],
            document.Projects.Select(p => (p.ProjectPath.Value, p.SolutionFolder)));
    }

    [Theory]
    [InlineData("<Solution><Project Path=\"A.csproj\"></Solution>")]
    [InlineData("not xml at all")]
    public void Slnx_InvalidXml_YieldsWarningNotException(string content)
    {
        var document = SolutionParser.ParseSlnx(RelativePath.Create("X.slnx"), FileId, content, MaxCharacters);

        Assert.Empty(document.Projects);
        Assert.Equal(DotNetCodes.InvalidXml, Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Slnx_EntityExpansionAttack_IsRejected()
    {
        const string billionLaughs = """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [
              <!ENTITY lol "lol">
              <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
              <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">
            ]>
            <Solution><Project Path="&lol3;.csproj" /></Solution>
            """;

        var document = SolutionParser.ParseSlnx(RelativePath.Create("X.slnx"), FileId, billionLaughs, MaxCharacters);

        Assert.Empty(document.Projects);
        Assert.Equal(DotNetCodes.InvalidXml, Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Slnx_ExternalEntity_IsRejected()
    {
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE s [ <!ENTITY secret SYSTEM "file:///etc/passwd"> ]>
            <Solution><Project Path="&secret;.csproj" /></Solution>
            """;

        var document = SolutionParser.ParseSlnx(RelativePath.Create("X.slnx"), FileId, xxe, MaxCharacters);

        Assert.Empty(document.Projects);
        Assert.Equal(DotNetCodes.InvalidXml, Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Slnx_DocumentAboveCharacterLimit_IsRejected()
    {
        var content = "<Solution>" + string.Concat(Enumerable.Repeat("<Project Path=\"A.csproj\" />", 100)) + "</Solution>";

        var document = SolutionParser.ParseSlnx(RelativePath.Create("X.slnx"), FileId, content, maxCharacters: 200);

        Assert.Equal(DotNetCodes.InvalidXml, Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Slnx_WrongRootOrMissingPath_AreReported()
    {
        var wrongRoot = SolutionParser.ParseSlnx(RelativePath.Create("X.slnx"), FileId, "<Project />", MaxCharacters);
        var missingPath = SolutionParser.ParseSlnx(RelativePath.Create("X.slnx"), FileId, "<Solution><Project /></Solution>", MaxCharacters);

        Assert.Equal(DotNetCodes.SolutionNotRecognized, Assert.Single(wrongRoot.Diagnostics).Code);
        Assert.Equal(DotNetCodes.SolutionMalformedEntry, Assert.Single(missingPath.Diagnostics).Code);
    }
}
