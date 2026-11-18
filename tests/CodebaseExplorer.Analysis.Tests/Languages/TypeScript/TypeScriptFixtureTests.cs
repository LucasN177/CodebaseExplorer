using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Languages.TypeScript;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.Node;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Analysis.Tests.ProjectSystems.Node;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.Languages.TypeScript;

/// <summary>ANALYSIS-010 against sections 6–8 (ts-monorepo-workspaces) and 4–6 (js-commonjs) of EXPECTED.md.</summary>
public sealed class TypeScriptFixtureTests
{
    private static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c7a3"));
    private static readonly AnalysisRunId Run = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000121"));

    internal static async Task<RepositoryAnalysis> AnalyzeAsync(string root)
    {
        var reader = new RepositoryFileReader();
        var pipeline = new AnalysisPipeline(
            new RepositoryScanner(reader, TimeProvider.System), reader, [new NodeProjectSystemAnalyzer()], [new TypeScriptAnalyzer()], TimeProvider.System);
        var result = await pipeline.AnalyzeAsync(new AnalysisRequest(Repository, new RepositoryRoot(root), Run), null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    /// <summary>Repository path of a file module (project root + qualified name).</summary>
    internal static string PathOf(ArchitectureModel model, CodeModule module)
    {
        var root = model.FindProject(module.ProjectId)!.RootDirectory;
        return root.IsRoot ? module.QualifiedName : $"{root.Value}/{module.QualifiedName}";
    }

    internal static string[] Imports(ArchitectureModel model) =>
        [.. model.Relationships
            .Where(r => r.Kind == RelationshipKind.Imports)
            .SelectMany(r => r.Evidence.Select(e =>
                $"{PathOf(model, model.FindModule(r.Source.Id)!)} -> "
                + (r.Target.Kind == ElementKind.Module ? PathOf(model, model.FindModule(r.Target.Id)!) : $"{model.FindExternalDependency(r.Target.Id)!.Ecosystem.ToString().ToLowerInvariant()}:{model.FindExternalDependency(r.Target.Id)!.Name}")
                + $" {e.Rule} {e.Level} {e.Location!.Value.StartLine}:{e.Location.Value.StartColumn}"))
            .Order(StringComparer.Ordinal)];

    internal static string[] Entities(ArchitectureModel model) =>
        [.. model.Entities
            .Select(e => $"{model.FindProject(e.ProjectId)!.Name} {e.Name} {e.Kind} {e.Visibility} {e.SymbolKey} {NodeFixtureTests.Location(model, e.Declarations[0])}")
            .Order(StringComparer.Ordinal)];

    internal static string[] Modules(ArchitectureModel model) =>
        [.. model.Modules
            .Select(m => $"{model.FindProject(m.ProjectId)!.Name} {m.Kind} {m.QualifiedName} parent={(m.ParentModuleId is { } p ? model.FindModule(p)!.QualifiedName : "-")}")
            .Order(StringComparer.Ordinal)];

    [Fact]
    public async Task TsMonorepo_ModulesMatchSection6()
    {
        var model = (await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("ts-monorepo-workspaces"))).Model;

        Assert.Equal(
            [
                "@acme/core Directory src parent=-",
                "@acme/core Directory src/orders parent=src",
                "@acme/core FileModule src/index.ts parent=src",
                "@acme/core FileModule src/orders/order-service.ts parent=src/orders",
                "@acme/core FileModule src/orders/order.ts parent=src/orders",
                "@acme/shared Directory src parent=-",
                "@acme/shared FileModule src/index.ts parent=src",
                "@acme/shared FileModule src/logging.ts parent=src",
                "@acme/shared FileModule src/money.ts parent=src",
                "web Directory src parent=-",
                "web Directory src/pages parent=src",
                "web FileModule src/App.test.tsx parent=src",
                "web FileModule src/App.tsx parent=src",
                "web FileModule src/main.tsx parent=src",
                "web FileModule src/pages/settings.tsx parent=src/pages",
            ],
            Modules(model));
    }

    [Fact]
    public async Task TsMonorepo_EntitiesMatchSection7()
    {
        var model = (await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("ts-monorepo-workspaces"))).Model;

        Assert.Equal(
            [
                "@acme/core Order Interface Exported src/orders/order.ts#Order packages/core/src/orders/order.ts:1:18",
                "@acme/core OrderId TypeAlias Exported src/orders/order.ts#OrderId packages/core/src/orders/order.ts:6:13",
                "@acme/core OrderService Class Exported src/orders/order-service.ts#OrderService packages/core/src/orders/order-service.ts:9:14",
                "@acme/core internalHelper Function NotExported src/orders/order-service.ts#~internalHelper packages/core/src/orders/order-service.ts:16:10",
                "@acme/shared formatMoney Function Exported src/money.ts#formatMoney packages/shared/src/money.ts:1:17",
                "@acme/shared log Function Exported src/logging.ts#log packages/shared/src/logging.ts:1:14",
                "web App Function Exported src/App.tsx#App apps/web/src/App.tsx:6:17",
                "web Settings Function Exported src/pages/settings.tsx#default apps/web/src/pages/settings.tsx:1:25",
            ],
            Entities(model));
    }

    [Fact]
    public async Task TsMonorepo_ImportsAndDiagnosticsMatchSection8()
    {
        var analysis = await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("ts-monorepo-workspaces"));
        var model = analysis.Model;

        Assert.Equal(
            [
                "apps/web/src/App.test.tsx -> apps/web/src/App.tsx ts.import.relative Confirmed 2:1",
                "apps/web/src/App.test.tsx -> npm:vitest ts.import.package Syntactic 1:1",
                "apps/web/src/App.tsx -> npm:chart.js ts.import.package-undeclared Heuristic 3:1",
                "apps/web/src/App.tsx -> npm:react ts.import.package Syntactic 1:1",
                "apps/web/src/App.tsx -> packages/core/src/index.ts ts.import.workspace Confirmed 2:1",
                "apps/web/src/main.tsx -> apps/web/src/App.tsx ts.import.relative Confirmed 1:1",
                "apps/web/src/main.tsx -> apps/web/src/pages/settings.tsx ts.dynamic-import.relative Confirmed 3:18",
                "packages/core/src/index.ts -> packages/core/src/orders/order-service.ts ts.export.relative Confirmed 1:1",
                "packages/core/src/index.ts -> packages/core/src/orders/order.ts ts.export.relative Confirmed 2:1",
                "packages/core/src/orders/order-service.ts -> npm:lodash ts.import.package Syntactic 3:1",
                "packages/core/src/orders/order-service.ts -> packages/core/src/orders/order.ts ts.import.relative Confirmed 1:1",
                "packages/core/src/orders/order-service.ts -> packages/shared/src/index.ts ts.import.workspace Confirmed 2:1",
                "packages/core/src/orders/order-service.ts -> packages/shared/src/logging.ts ts.import.path-alias Confirmed 4:1",
                "packages/shared/src/index.ts -> packages/shared/src/money.ts ts.export.relative Confirmed 1:1",
            ],
            Imports(model));
        Assert.Contains(model.ExternalDependencies, d => d.Name == "chart.js");

        var languageDiagnostics = analysis.Diagnostics.Where(d => d.Analyzer == TypeScriptCodes.Analyzer).ToArray();
        var unresolved = Assert.Single(languageDiagnostics);
        Assert.Equal((DiagnosticSeverity.Warning, TypeScriptCodes.ImportUnresolved, "apps/web/src/App.tsx:4:1"),
            (unresolved.Severity, unresolved.Code, NodeFixtureTests.Location(model, unresolved.Location)));
    }

    [Fact]
    public async Task JsCommonJs_MatchesSections4To6()
    {
        var analysis = await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("js-commonjs"));
        var model = analysis.Model;

        Assert.Equal(
            [
                "legacy-tool Directory lib parent=-",
                "legacy-tool Directory lib/routes parent=lib",
                "legacy-tool Directory lib/util parent=lib",
                "legacy-tool Directory test parent=-",
                "legacy-tool FileModule lib/index.js parent=lib",
                "legacy-tool FileModule lib/routes/index.js parent=lib/routes",
                "legacy-tool FileModule lib/util/helper.js parent=lib/util",
                "legacy-tool FileModule test/index.test.js parent=test",
            ],
            Modules(model));
        Assert.Equal(
            [
                "legacy-tool Helper Class Exported lib/util/helper.js#Helper lib/util/helper.js:1:7",
                "legacy-tool createRouter Function Exported lib/routes/index.js#createRouter lib/routes/index.js:3:10",
                "legacy-tool start Function Exported lib/index.js#start lib/index.js:5:10",
            ],
            Entities(model));
        Assert.Equal(
            [
                "lib/index.js -> lib/routes/index.js ts.require.relative Confirmed 2:26",
                "lib/index.js -> npm:express ts.require.package Syntactic 1:17",
                "lib/routes/index.js -> lib/util/helper.js ts.require.relative Confirmed 1:16",
                "test/index.test.js -> lib/index.js ts.require.relative Confirmed 3:19",
            ],
            Imports(model));

        var computed = Assert.Single(analysis.Diagnostics);
        Assert.Equal((DiagnosticSeverity.Info, TypeScriptCodes.ImportComputed, "lib/routes/index.js:5:18"),
            (computed.Severity, computed.Code, NodeFixtureTests.Location(model, computed.Location)));
    }

    [Fact]
    public async Task MixedPolyglot_FrontendMatchesSection4()
    {
        var analysis = await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("mixed-polyglot"));
        var model = analysis.Model;

        Assert.Equal(["frontend fetchOrders Function Exported src/api.ts#fetchOrders frontend/src/api.ts:3:23"], Entities(model));
        Assert.Equal(["frontend/src/api.ts -> npm:axios ts.import.package Syntactic 1:1"], Imports(model));
        Assert.Empty(analysis.Diagnostics);
    }
}
