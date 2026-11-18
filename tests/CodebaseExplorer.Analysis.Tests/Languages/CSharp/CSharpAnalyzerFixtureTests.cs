using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Languages.CSharp;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.DotNet;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.Languages.CSharp;

/// <summary>ANALYSIS-005: modules and entities of fixture <c>dotnet-advanced</c>, checked against EXPECTED.md §8.</summary>
public sealed class CSharpAnalyzerFixtureTests
{
    private const string Core = "src/Advanced.Core";
    private const string Services = "src/Advanced.Services";

    private static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c6d9"));
    private static readonly AnalysisRunId Run = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000044"));

    /// <summary>EXPECTED.md §8.3 and §8.4: key → (project, kind, visibility, modifiers, declarations, containing type key).</summary>
    private static readonly (string Key, string Project, EntityKind Kind, Visibility Visibility, EntityModifiers Modifiers, string[] Declarations, string? Container)[] ExpectedEntities =
    [
        ("T:Advanced.Core.Entities.Entity`1", "Advanced.Core", EntityKind.Class, Visibility.Public, EntityModifiers.Abstract | EntityModifiers.Generic, [$"{Core}/Entities/Entity.cs:3:23"], null),
        ("M:Advanced.Core.Entities.Entity`1.#ctor(`0)", "Advanced.Core", EntityKind.Constructor, Visibility.Protected, EntityModifiers.None, [$"{Core}/Entities/Entity.cs:6:15"], "T:Advanced.Core.Entities.Entity`1"),
        ("P:Advanced.Core.Entities.Entity`1.Id", "Advanced.Core", EntityKind.Property, Visibility.Public, EntityModifiers.None, [$"{Core}/Entities/Entity.cs:11:16"], "T:Advanced.Core.Entities.Entity`1"),
        ("T:Advanced.Core.Entities.Customer", "Advanced.Core", EntityKind.Class, Visibility.Public, EntityModifiers.Partial, [$"{Core}/Entities/Customer.Orders.cs:3:22", $"{Core}/Entities/Customer.cs:5:22"], null),
        ("M:Advanced.Core.Entities.Customer.#ctor(System.Guid,System.String)", "Advanced.Core", EntityKind.Constructor, Visibility.Public, EntityModifiers.None, [$"{Core}/Entities/Customer.cs:7:12"], "T:Advanced.Core.Entities.Customer"),
        ("P:Advanced.Core.Entities.Customer.Name", "Advanced.Core", EntityKind.Property, Visibility.Public, EntityModifiers.None, [$"{Core}/Entities/Customer.cs:13:19"], "T:Advanced.Core.Entities.Customer"),
        ("P:Advanced.Core.Entities.Customer.ModifiedAt", "Advanced.Core", EntityKind.Property, Visibility.Public, EntityModifiers.None, [$"{Core}/Entities/Customer.cs:15:27"], "T:Advanced.Core.Entities.Customer"),
        ("F:Advanced.Core.Entities.Customer._orders", "Advanced.Core", EntityKind.Field, Visibility.Private, EntityModifiers.None, [$"{Core}/Entities/Customer.Orders.cs:5:41"], "T:Advanced.Core.Entities.Customer"),
        ("P:Advanced.Core.Entities.Customer.Orders", "Advanced.Core", EntityKind.Property, Visibility.Public, EntityModifiers.None, [$"{Core}/Entities/Customer.Orders.cs:7:40"], "T:Advanced.Core.Entities.Customer"),
        ("M:Advanced.Core.Entities.Customer.AddOrder(System.Decimal)", "Advanced.Core", EntityKind.Method, Visibility.Public, EntityModifiers.None, [$"{Core}/Entities/Customer.Orders.cs:9:17"], "T:Advanced.Core.Entities.Customer"),
        ("T:Advanced.Core.Entities.Customer.OrderSummary", "Advanced.Core", EntityKind.Class, Visibility.Public, EntityModifiers.Sealed, [$"{Core}/Entities/Customer.Orders.cs:14:25"], "T:Advanced.Core.Entities.Customer"),
        ("M:Advanced.Core.Entities.Customer.OrderSummary.#ctor(System.Decimal)", "Advanced.Core", EntityKind.Constructor, Visibility.Public, EntityModifiers.None, [$"{Core}/Entities/Customer.Orders.cs:16:16"], "T:Advanced.Core.Entities.Customer.OrderSummary"),
        ("P:Advanced.Core.Entities.Customer.OrderSummary.Total", "Advanced.Core", EntityKind.Property, Visibility.Public, EntityModifiers.None, [$"{Core}/Entities/Customer.Orders.cs:21:24"], "T:Advanced.Core.Entities.Customer.OrderSummary"),
        ("T:Advanced.Core.Abstractions.IAuditable", "Advanced.Core", EntityKind.Interface, Visibility.Public, EntityModifiers.None, [$"{Core}/Abstractions/IAuditable.cs:3:18"], null),
        ("P:Advanced.Core.Abstractions.IAuditable.ModifiedAt", "Advanced.Core", EntityKind.Property, Visibility.Public, EntityModifiers.None, [$"{Core}/Abstractions/IAuditable.cs:5:20"], "T:Advanced.Core.Abstractions.IAuditable"),
        ("T:Advanced.Core.Abstractions.IRepository`1", "Advanced.Core", EntityKind.Interface, Visibility.Public, EntityModifiers.Generic, [$"{Core}/Abstractions/IRepository.cs:5:18"], null),
        ("M:Advanced.Core.Abstractions.IRepository`1.Find(System.Guid)", "Advanced.Core", EntityKind.Method, Visibility.Public, EntityModifiers.None, [$"{Core}/Abstractions/IRepository.cs:8:8"], "T:Advanced.Core.Abstractions.IRepository`1"),
        ("M:Advanced.Core.Abstractions.IRepository`1.Add(`0)", "Advanced.Core", EntityKind.Method, Visibility.Public, EntityModifiers.None, [$"{Core}/Abstractions/IRepository.cs:10:10"], "T:Advanced.Core.Abstractions.IRepository`1"),
        ("T:Advanced.Core.Abstractions.ResultKind", "Advanced.Core", EntityKind.Enum, Visibility.Public, EntityModifiers.None, [$"{Core}/Abstractions/Result.cs:3:13"], null),
        ("F:Advanced.Core.Abstractions.ResultKind.Success", "Advanced.Core", EntityKind.Constant, Visibility.Public, EntityModifiers.None, [$"{Core}/Abstractions/Result.cs:5:5"], "T:Advanced.Core.Abstractions.ResultKind"),
        ("F:Advanced.Core.Abstractions.ResultKind.Failure", "Advanced.Core", EntityKind.Constant, Visibility.Public, EntityModifiers.None, [$"{Core}/Abstractions/Result.cs:6:5"], "T:Advanced.Core.Abstractions.ResultKind"),
        ("T:Advanced.Core.Abstractions.Result`1", "Advanced.Core", EntityKind.Struct, Visibility.Public, EntityModifiers.Generic, [$"{Core}/Abstractions/Result.cs:9:24"], null),
        ("M:Advanced.Core.Abstractions.Result`1.#ctor(Advanced.Core.Abstractions.ResultKind,`0)", "Advanced.Core", EntityKind.Constructor, Visibility.Public, EntityModifiers.None, [$"{Core}/Abstractions/Result.cs:11:12"], "T:Advanced.Core.Abstractions.Result`1"),
        ("P:Advanced.Core.Abstractions.Result`1.Kind", "Advanced.Core", EntityKind.Property, Visibility.Public, EntityModifiers.None, [$"{Core}/Abstractions/Result.cs:17:23"], "T:Advanced.Core.Abstractions.Result`1"),
        ("P:Advanced.Core.Abstractions.Result`1.Value", "Advanced.Core", EntityKind.Property, Visibility.Public, EntityModifiers.None, [$"{Core}/Abstractions/Result.cs:19:15"], "T:Advanced.Core.Abstractions.Result`1"),
        ("M:Advanced.Core.Abstractions.Result`1.Ok(`0)", "Advanced.Core", EntityKind.Method, Visibility.Public, EntityModifiers.Static, [$"{Core}/Abstractions/Result.cs:21:29"], "T:Advanced.Core.Abstractions.Result`1"),
        ("T:Advanced.Core.Abstractions.ResultHandler`1", "Advanced.Core", EntityKind.Delegate, Visibility.Public, EntityModifiers.Generic, [$"{Core}/Abstractions/Result.cs:24:22"], null),
        ("T:Advanced.Services.Customers.ICustomerService", "Advanced.Services", EntityKind.Interface, Visibility.Public, EntityModifiers.None, [$"{Services}/Customers/ICustomerService.cs:6:18"], null),
        ("M:Advanced.Services.Customers.ICustomerService.Create(System.String)", "Advanced.Services", EntityKind.Method, Visibility.Public, EntityModifiers.None, [$"{Services}/Customers/ICustomerService.cs:8:22"], "T:Advanced.Services.Customers.ICustomerService"),
        ("T:Advanced.Services.Customers.CustomerService", "Advanced.Services", EntityKind.Class, Visibility.Internal, EntityModifiers.Sealed, [$"{Services}/Customers/CustomerService.cs:6:23"], null),
        ("F:Advanced.Services.Customers.CustomerService._repository", "Advanced.Services", EntityKind.Field, Visibility.Private, EntityModifiers.None, [$"{Services}/Customers/CustomerService.cs:8:44"], "T:Advanced.Services.Customers.CustomerService"),
        ("M:Advanced.Services.Customers.CustomerService.#ctor(Advanced.Core.Abstractions.IRepository{Advanced.Core.Entities.Customer})", "Advanced.Services", EntityKind.Constructor, Visibility.Public, EntityModifiers.None, [$"{Services}/Customers/CustomerService.cs:10:12"], "T:Advanced.Services.Customers.CustomerService"),
        ("M:Advanced.Services.Customers.CustomerService.Create(System.String)", "Advanced.Services", EntityKind.Method, Visibility.Public, EntityModifiers.None, [$"{Services}/Customers/CustomerService.cs:15:29"], "T:Advanced.Services.Customers.CustomerService"),
        ("T:Advanced.Services.Customers.InMemoryCustomerRepository", "Advanced.Services", EntityKind.Class, Visibility.Internal, EntityModifiers.Sealed, [$"{Services}/Customers/InMemoryCustomerRepository.cs:6:23"], null),
        ("F:Advanced.Services.Customers.InMemoryCustomerRepository._items", "Advanced.Services", EntityKind.Field, Visibility.Private, EntityModifiers.None, [$"{Services}/Customers/InMemoryCustomerRepository.cs:8:49"], "T:Advanced.Services.Customers.InMemoryCustomerRepository"),
        ("M:Advanced.Services.Customers.InMemoryCustomerRepository.Find(System.Guid)", "Advanced.Services", EntityKind.Method, Visibility.Public, EntityModifiers.None, [$"{Services}/Customers/InMemoryCustomerRepository.cs:10:22"], "T:Advanced.Services.Customers.InMemoryCustomerRepository"),
        ("M:Advanced.Services.Customers.InMemoryCustomerRepository.Add(Advanced.Core.Entities.Customer)", "Advanced.Services", EntityKind.Method, Visibility.Public, EntityModifiers.None, [$"{Services}/Customers/InMemoryCustomerRepository.cs:12:17"], "T:Advanced.Services.Customers.InMemoryCustomerRepository"),
        ("T:Advanced.Services.ServiceCollectionExtensions", "Advanced.Services", EntityKind.Class, Visibility.Public, EntityModifiers.Static, [$"{Services}/ServiceCollectionExtensions.cs:8:21"], null),
        ("M:Advanced.Services.ServiceCollectionExtensions.AddAdvancedServices(IServiceCollection)", "Advanced.Services", EntityKind.Method, Visibility.Public, EntityModifiers.Static, [$"{Services}/ServiceCollectionExtensions.cs:10:38"], "T:Advanced.Services.ServiceCollectionExtensions"),
        ("T:Advanced.Services.ServiceCollectionExtensions.Catalog", "Advanced.Services", EntityKind.Class, Visibility.Private, EntityModifiers.Sealed, [$"{Services}/ServiceCollectionExtensions.cs:23:26"], "T:Advanced.Services.ServiceCollectionExtensions"),
        ("M:Advanced.Services.ServiceCollectionExtensions.Catalog.AddSingleton(System.String)", "Advanced.Services", EntityKind.Method, Visibility.Public, EntityModifiers.None, [$"{Services}/ServiceCollectionExtensions.cs:25:21"], "T:Advanced.Services.ServiceCollectionExtensions.Catalog"),
    ];

    internal static async Task<RepositoryAnalysis> AnalyzeAsync(string fixture)
    {
        var reader = new RepositoryFileReader();
        var pipeline = new AnalysisPipeline(
            new RepositoryScanner(reader, TimeProvider.System), reader, [new DotNetProjectSystemAnalyzer()], [], TimeProvider.System, [new CSharpAnalyzer()]);
        var result = await pipeline.AnalyzeAsync(
            new AnalysisRequest(Repository, new RepositoryRoot(TestRepositoryCatalog.GetFixturePath(fixture)), Run), null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private static string Location(ArchitectureModel model, SourceLocation location) =>
        $"{model.FindFile(location.FileId)!.Path}:{location.StartLine}:{location.StartColumn}";

    [Fact]
    public async Task Modules_MatchExpectedSection8_1()
    {
        var model = (await AnalyzeAsync("dotnet-advanced")).Model;

        var modules = model.Modules
            .Select(m => (model.FindProject(m.ProjectId)!.Name, m.QualifiedName, m.ParentModuleId is { } parent ? model.FindModule(parent)!.QualifiedName : null))
            .Order()
            .ToArray();

        Assert.Equal(
            [
                ("Advanced.Core", "Advanced", null),
                ("Advanced.Core", "Advanced.Core", "Advanced"),
                ("Advanced.Core", "Advanced.Core.Abstractions", "Advanced.Core"),
                ("Advanced.Core", "Advanced.Core.Entities", "Advanced.Core"),
                ("Advanced.Services", "Advanced", null),
                ("Advanced.Services", "Advanced.Services", "Advanced"),
                ("Advanced.Services", "Advanced.Services.Customers", "Advanced.Services"),
            ],
            modules);
    }

    [Fact]
    public async Task Entities_MatchExpectedSections8_3And8_4()
    {
        var model = (await AnalyzeAsync("dotnet-advanced")).Model;
        var byKey = model.Entities.ToDictionary(e => e.SymbolKey);

        Assert.Equal(ExpectedEntities.Select(e => e.Key).Order(StringComparer.Ordinal), byKey.Keys.Order(StringComparer.Ordinal));
        Assert.All(ExpectedEntities, expected =>
        {
            var entity = byKey[expected.Key];
            Assert.Equal(expected.Project, model.FindProject(entity.ProjectId)!.Name);
            Assert.Equal(expected.Kind, entity.Kind);
            Assert.Equal(expected.Visibility, entity.Visibility);
            Assert.Equal(expected.Modifiers, entity.Modifiers);
            Assert.Equal(expected.Declarations, entity.Declarations.Select(d => Location(model, d)));
            Assert.Equal(expected.Container, entity.ContainingEntityId is { } container ? model.FindEntity(container)!.SymbolKey : null);
            Assert.Equal("csharp", entity.Language.Value);
        });
    }

    [Fact]
    public async Task EntitiesBelongToTheNamespaceOfTheirOutermostType()
    {
        var model = (await AnalyzeAsync("dotnet-advanced")).Model;

        string ModuleOf(string key) => model.FindModule(model.Entities.Single(e => e.SymbolKey == key).ModuleId)!.QualifiedName;

        Assert.Equal("Advanced.Core.Entities", ModuleOf("P:Advanced.Core.Entities.Customer.OrderSummary.Total"));
        Assert.Equal("Advanced.Services", ModuleOf("T:Advanced.Services.ServiceCollectionExtensions.Catalog"));
        Assert.Equal("Advanced.Core.Abstractions", ModuleOf("F:Advanced.Core.Abstractions.ResultKind.Success"));
    }

    [Fact]
    public async Task Analysis_ReportsNoCSharpDiagnostics_AndRecordsTheAnalyzer()
    {
        var analysis = await AnalyzeAsync("dotnet-advanced");

        Assert.DoesNotContain(analysis.Diagnostics, d => d.Analyzer?.Value == CSharpCodes.AnalyzerName);
        Assert.Equal(CSharpCodes.Version, analysis.AnalyzerVersions[CSharpCodes.AnalyzerName]);
        Assert.Equal(7, analysis.Statistics.Modules);
        Assert.Equal(41, analysis.Statistics.Entities);
    }

    [Fact]
    public async Task SymbolKeys_AreStableAcrossRuns()
    {
        var first = (await AnalyzeAsync("dotnet-advanced")).Model.Entities.Select(e => (e.Id.Value, e.SymbolKey)).Order().ToArray();
        var second = (await AnalyzeAsync("dotnet-advanced")).Model.Entities.Select(e => (e.Id.Value, e.SymbolKey)).Order().ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task LayeredFixture_IsAnalyzedWithoutErrors()
    {
        var analysis = await AnalyzeAsync("dotnet-layered");

        Assert.DoesNotContain(analysis.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(analysis.Model.Entities, e => e.SymbolKey == "T:Shop.Domain.Orders.Order");
        Assert.Contains(analysis.Model.Entities, e => e.SymbolKey == "T:Shop.Application.Orders.PlaceOrderHandler");
    }
}
