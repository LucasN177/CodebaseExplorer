using CodebaseExplorer.Analysis.ProjectSystems.Python;
using CodebaseExplorer.Analysis.Tests.ProjectSystems.Node;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.ProjectSystems.Python;

/// <summary>ANALYSIS-011: PEP 508, requirements files, setup.cfg, pyproject.toml variants and hostile input.</summary>
public sealed class PythonManifestTests : IDisposable
{
    private static readonly RelativePath File = RelativePath.Create("pyproject.toml");

    private readonly TempDirectory _repository = new();

    public void Dispose() => _repository.Dispose();

    [Theory]
    [InlineData("requests", "requests", null, null)]
    [InlineData("requests>=2.31,<3", "requests", ">=2.31,<3", null)]
    [InlineData("Django[argon2,bcrypt] ~= 5.0", "Django", "~= 5.0", null)]
    [InlineData("pywin32 >=306 ; sys_platform == 'win32'", "pywin32", ">=306", "sys_platform == 'win32'")]
    [InlineData("requests (>=2)", "requests", ">=2", null)]
    [InlineData("zope.interface==6.0", "zope.interface", "==6.0", null)]
    [InlineData("pip @ https://example.invalid/pip.zip", "pip", null, null)]
    public void Pep508_extracts_name_specifier_and_marker(string text, string name, string? spec, string? marker)
    {
        var requirement = Pep508.Parse(text);

        Assert.NotNull(requirement);
        Assert.Equal((name, spec, marker), (requirement.Name, requirement.VersionSpec, requirement.Marker));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-requests")]
    [InlineData("requests garbage")]
    [InlineData("requests[unclosed")]
    public void Pep508_rejects_invalid_requirements(string text) => Assert.Null(Pep508.Parse(text));

    [Fact]
    public void Requirements_file_handles_comments_continuations_options_and_urls()
    {
        var includes = new List<RequirementsInclude>();
        var facts = RequirementsFile.Read(RelativePath.Create("requirements.txt"), string.Join('\n',
            "# comment",
            "flask==3.0 # inline comment",
            "numpy>=1.26 \\",
            "    ,<2",
            "-e ./local",
            "--requirement=base.txt",
            "-r other.txt",
            "git+https://example.invalid/repo.git",
            "url#fragment==1"), includes);

        Assert.Equal(["flask ==3.0", "numpy >=1.26     ,<2"], facts.Dependencies.Select(d => $"{d.Name} {d.Attributes.GetValueOrDefault("versionSpec")}"));
        Assert.Equal([3], facts.Dependencies.Where(d => d.Name == "numpy").Select(d => d.Line));
        Assert.Equal([("base.txt", 6), ("other.txt", 7)], includes.Select(i => (i.Path, i.Line)));
        Assert.Equal(
            [PythonCodes.OptionIgnored, PythonCodes.RequirementUnsupported, PythonCodes.RequirementInvalid],
            facts.Problems.Select(p => p.Code));
    }

    [Fact]
    public void SetupCfg_reads_metadata_options_and_extras()
    {
        var facts = SetupCfgFile.Read(RelativePath.Create("setup.cfg"), string.Join('\n',
            "[metadata]",
            "name = legacy-pkg",
            "version = 2.1",
            "",
            "[options]",
            "python_requires = >=3.9",
            "install_requires =",
            "    click>=8",
            "    # a comment",
            "    rich",
            "",
            "[options.extras_require]",
            "docs = sphinx"));

        Assert.Equal(("legacy-pkg", "2.1", ">=3.9"), (facts.Name, facts.Version, facts.RequiresPython));
        Assert.Equal(
            ["click setup.cfg.install_requires 8:5", "rich setup.cfg.install_requires 10:5", "sphinx setup.cfg.extras_require 13:8"],
            facts.Dependencies.Select(d => $"{d.Name} {d.Rule} {d.Line}:{d.Column}"));
        Assert.Equal("docs", facts.Dependencies[2].Attributes["extra"]);
    }

    [Fact]
    public void PyProject_reads_poetry_inline_tables_dev_dependencies_and_dependency_groups()
    {
        var facts = PyProjectFile.Read(File, string.Join('\n',
            "[tool.poetry]",
            "name = \"p\"",
            "[tool.poetry.dependencies]",
            "python = \">=3.10\"",
            "fastapi = { version = \"^0.110\", extras = [\"all\"] }",
            "local = { path = \"../local\" }",
            "[tool.poetry.dev-dependencies]",
            "mypy = \"*\"",
            "[dependency-groups]",
            "test = [\"coverage>=7\", { include-group = \"lint\" }]",
            "[project]",
            "name = \"ignored-because-poetry-came-first\""));

        Assert.Equal(("p", ">=3.10"), (facts.Name, facts.RequiresPython));
        Assert.Equal(
            [
                "fastapi pyproject.poetry.dependencies versionSpec=^0.110",
                "local pyproject.poetry.dependencies ",
                "mypy pyproject.poetry.dev-dependencies devOnly=true",
                "coverage pyproject.dependency-groups group=test,versionSpec=>=7",
            ],
            facts.Dependencies.Select(d => $"{d.Name} {d.Rule} {string.Join(',', d.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={a.Value}"))}"));
    }

    [Theory]
    [InlineData("[project\nname = ")]
    [InlineData("[project]\nname = 'a'\n[project]\nname = 'b'\n")]
    public void PyProject_invalid_toml_is_a_warning(string content)
    {
        var facts = PyProjectFile.Read(File, content);

        Assert.Equal(PythonCodes.ManifestInvalid, Assert.Single(facts.Problems).Code);
        Assert.Null(facts.Name);
    }

    [Fact]
    public void PyProject_too_deep_nesting_is_rejected_without_crashing()
    {
        var deep = "x = " + new string('[', 100_000) + new string(']', 100_000) + "\n";
        var inline = "y = " + string.Concat(Enumerable.Repeat("{a=", 100_000)) + "1" + new string('}', 100_000) + "\n";

        Assert.Equal(PythonCodes.ManifestInvalid, Assert.Single(PyProjectFile.Read(File, deep).Problems).Code);
        Assert.Equal(PythonCodes.ManifestInvalid, Assert.Single(PyProjectFile.Read(File, inline).Problems).Code);
    }

    [Fact]
    public async Task Requirements_includes_are_followed_inside_the_repository_once()
    {
        _repository.WriteText("requirements.txt", "-r requirements/base.txt\n-r missing.txt\nflask\n");
        _repository.WriteText("requirements/base.txt", "-r ../requirements.txt\nsqlalchemy>=2\n");

        var analysis = await PythonFixtureTests.AnalyzeAsync(_repository.FullPath);

        Assert.Equal(["flask", "sqlalchemy"], analysis.Model.ExternalDependencies.Select(d => d.Name).Order(StringComparer.Ordinal));
        var sqlalchemy = analysis.Model.Relationships.Single(r => analysis.Model.FindExternalDependency(r.Target.Id)!.Name == "sqlalchemy");
        Assert.Equal("requirements/base.txt:2:1", NodeFixtureTests.Location(analysis.Model, sqlalchemy.Evidence[0].Location));
        Assert.Equal(
            [PythonCodes.IncludeMissing, PythonCodes.IncludeDepth],
            analysis.Diagnostics.Select(d => d.Code).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SetupPy_is_detected_and_never_executed_and_projects_reference_each_other()
    {
        _repository.WriteText("lib/setup.py", "raise SystemExit('never run')\n");
        _repository.WriteText("lib/setup.cfg", "[metadata]\nname = Shared_Lib\n");
        _repository.WriteText("app/pyproject.toml", "[project]\nname = \"app\"\ndependencies = [\"shared-lib>=1\"]\n");

        var analysis = await PythonFixtureTests.AnalyzeAsync(_repository.FullPath);
        var model = analysis.Model;

        var lib = model.Projects.Single(p => p.Name == "Shared_Lib");
        Assert.Equal(("lib/setup.cfg", "setup.cfg;setup.py", "true"),
            (model.FindFile(lib.ProjectFileId!.Value)!.Path.Value, lib.Properties["manifests"], lib.Properties["setupPy"]));
        Assert.Equal(
            ["ProjectReference app -> Shared_Lib Confirmed pyproject.project.dependencies@app/pyproject.toml:3:17 {versionSpec=>=1}"],
            NodeFixtureTests.Describe(model, RelationshipKind.ProjectReference, RelationshipKind.PackageReference));
        var diagnostic = Assert.Single(analysis.Diagnostics);
        Assert.Equal((DiagnosticSeverity.Info, PythonCodes.SetupPyNotExecuted), (diagnostic.Severity, diagnostic.Code));
    }

    [Fact]
    public async Task A_folder_with_only_requirements_is_a_project_named_after_the_folder()
    {
        _repository.WriteText("scripts/requirements.txt", "boto3\n");

        var project = Assert.Single((await PythonFixtureTests.AnalyzeAsync(_repository.FullPath)).Model.Projects);

        Assert.Equal(("scripts", "scripts/requirements.txt"), (project.Name, project.ProjectFileId is null ? null : "scripts/requirements.txt"));
        Assert.False(project.Properties.ContainsKey("version"));
    }
}
