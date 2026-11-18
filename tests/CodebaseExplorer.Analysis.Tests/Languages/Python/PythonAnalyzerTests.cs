using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Languages.Python;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.Python;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Analysis.Tests.Languages.TypeScript;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.Languages.Python;

/// <summary>ANALYSIS-012 against sections 5–7 of python-src-layout's EXPECTED.md and edge cases.</summary>
public sealed class PythonAnalyzerTests : IDisposable
{
    private static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c8b4"));
    private static readonly AnalysisRunId Run = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000131"));

    private readonly TempDirectory _repository = new();

    public void Dispose() => _repository.Dispose();

    private static async Task<RepositoryAnalysis> AnalyzeAsync(string root)
    {
        var reader = new RepositoryFileReader();
        var pipeline = new AnalysisPipeline(
            new RepositoryScanner(reader, TimeProvider.System), reader, [new PythonProjectSystemAnalyzer()], [new PythonAnalyzer()], TimeProvider.System);
        var result = await pipeline.AnalyzeAsync(new AnalysisRequest(Repository, new RepositoryRoot(root), Run), null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    /// <summary>Imports by dotted module names (not paths).</summary>
    private static string[] Imports(Domain.Model.ArchitectureModel model) =>
        [.. model.Relationships
            .Where(r => r.Kind == RelationshipKind.Imports)
            .SelectMany(r => r.Evidence.Select(e =>
                $"{model.FindModule(r.Source.Id)!.QualifiedName} -> "
                + (r.Target.Kind == ElementKind.Module ? $"{model.FindModule(r.Target.Id)!.QualifiedName} ({model.FindModule(r.Target.Id)!.Kind})" : $"pypi:{model.FindExternalDependency(r.Target.Id)!.Name}")
                + $" {e.Rule} {e.Level} {Tests.ProjectSystems.Node.NodeFixtureTests.Location(model, e.Location)}"))
            .Order(StringComparer.Ordinal)];

    [Fact]
    public async Task PythonSrcLayout_MatchesSections5To7()
    {
        var analysis = await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("python-src-layout"));
        var model = analysis.Model;

        Assert.Equal(
            [
                "Inventory-Service FileModule inventory.models parent=inventory",
                "Inventory-Service FileModule inventory.service parent=inventory",
                "Inventory-Service FileModule inventory.storage.db parent=inventory.storage",
                "Inventory-Service FileModule tests.test_service parent=tests",
                "Inventory-Service Package inventory parent=-",
                "Inventory-Service Package inventory.storage parent=inventory",
                "Inventory-Service Package tests parent=-",
            ],
            TypeScriptFixtureTests.Modules(model));
        Assert.Equal(
            [
                "Inventory-Service Database Class Public inventory.storage.db:Database src/inventory/storage/db.py:4:7",
                "Inventory-Service InventoryService Class Public inventory.service:InventoryService src/inventory/service.py:11:7",
                "Inventory-Service Item Class Public inventory.models:Item src/inventory/models.py:7:7",
                "Inventory-Service _helper Function Private inventory.service:_helper src/inventory/service.py:23:5",
                "Inventory-Service make_item Function Public inventory.models:make_item src/inventory/models.py:12:5",
                "Inventory-Service test_service_requires_database Function Public tests.test_service:test_service_requires_database tests/test_service.py:6:5",
            ],
            TypeScriptFixtureTests.Entities(model));
        Assert.Equal(
            [
                "inventory -> inventory.models (FileModule) py.import.relative Confirmed src/inventory/__init__.py:1:1",
                "inventory.models -> pypi:attrs py.import.package Syntactic src/inventory/models.py:3:1",
                "inventory.service -> inventory.models (FileModule) py.import.relative Confirmed src/inventory/service.py:7:1",
                "inventory.service -> inventory.storage (Package) py.import.relative Confirmed src/inventory/service.py:6:1",
                "inventory.service -> inventory.storage.db (FileModule) py.import.absolute Confirmed src/inventory/service.py:8:1",
                "inventory.service -> pypi:beautifulsoup4 py.import.package-undeclared Heuristic src/inventory/service.py:4:1",
                "inventory.service -> pypi:pyyaml py.import.package-mapped Heuristic src/inventory/service.py:3:1",
                "inventory.service -> pypi:requests py.import.package Syntactic src/inventory/service.py:2:1",
                "inventory.storage.db -> inventory.models (FileModule) py.import.relative Confirmed src/inventory/storage/db.py:1:1",
                "tests.test_service -> inventory.service (FileModule) py.import.absolute Confirmed tests/test_service.py:3:1",
                "tests.test_service -> pypi:pytest py.import.package Syntactic tests/test_service.py:1:1",
            ],
            Imports(model));
        Assert.Equal("PyYAML", model.ExternalDependencies.Single(d => d.Name == "pyyaml").DisplayName);
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Analyzer?.Value == PythonAnalyzer.AnalyzerName);
    }

    [Fact]
    public async Task MixedPolyglot_ReportToolMatchesSection4()
    {
        var model = (await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("mixed-polyglot"))).Model;

        Assert.Equal(
            ["report-tool FileModule report_tool.report parent=report_tool", "report-tool Package report_tool parent=-"],
            TypeScriptFixtureTests.Modules(model));
        Assert.Equal(["report-tool build_report Function Public report_tool.report:build_report tools/py/report_tool/report.py:4:5"], TypeScriptFixtureTests.Entities(model));
        Assert.Equal(["report_tool.report -> pypi:pandas py.import.package Syntactic tools/py/report_tool/report.py:1:1"], Imports(model));
    }

    [Fact]
    public async Task Edge_cases_nested_imports_star_aliases_async_decorators_and_unresolved()
    {
        _repository.WriteText("requirements.txt", "requests\nPillow\n");
        _repository.WriteText("app/__init__.py", "from .core import *\n");
        _repository.WriteText("app/core.py", string.Join('\n',
            "import os.path as p",
            "import app.util, PIL.Image",
            "from __future__ import annotations",
            "from ... import nothing",
            "from .missing import x",
            "",
            "@decorator",
            "async def fetch():",
            "    import requests",
            "",
            "class Data:",
            "    pass",
            "",
            "def Data(): ...",
            "try:",
            "    import ujson as json",
            "except ImportError:",
            "    import json"));
        _repository.WriteText("app/util.py", "from .. import nothing_either\n");
        _repository.WriteText("main.py", "import app\nfrom app.core import fetch\n");

        var analysis = await AnalyzeAsync(_repository.FullPath);
        var model = analysis.Model;

        Assert.Equal(
            [
                "app -> app.core (FileModule) py.import.relative Confirmed app/__init__.py:1:1",
                "app.core -> app.util (FileModule) py.import.absolute Confirmed app/core.py:2:1",
                "app.core -> pypi:pillow py.import.package-mapped Heuristic app/core.py:2:1",
                "app.core -> pypi:requests py.import.package Syntactic app/core.py:9:5",
                "app.core -> pypi:ujson py.import.package-undeclared Heuristic app/core.py:16:5",
                "main -> app (Package) py.import.absolute Confirmed main.py:1:1",
                "main -> app.core (FileModule) py.import.absolute Confirmed main.py:2:1",
            ],
            Imports(model));
        var fetch = model.Entities.Single(e => e.Name == "fetch");
        Assert.Equal((EntityKind.Function, EntityModifiers.Async), (fetch.Kind, fetch.Modifiers));
        Assert.Single(model.Entities, e => e.Name == "Data"); // same name declared twice in one module: one entity
        Assert.Equal(
            [PythonAnalyzer.ImportUnresolved, PythonAnalyzer.ImportUnresolved, PythonAnalyzer.ImportUnresolved],
            analysis.Diagnostics.Where(d => d.Analyzer?.Value == PythonAnalyzer.AnalyzerName).Select(d => d.Code));
        Assert.All(model.Relationships.Where(r => r.Kind == RelationshipKind.Imports && r.Target.Kind == ElementKind.ExternalDependency && r.Level == EvidenceLevel.Heuristic),
            r => Assert.StartsWith("py.import.package-", r.Evidence[0].Rule, StringComparison.Ordinal));
    }
}
