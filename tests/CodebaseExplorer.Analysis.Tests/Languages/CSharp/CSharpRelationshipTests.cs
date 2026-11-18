using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Languages.CSharp;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.DotNet;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.Languages.CSharp;

/// <summary>ANALYSIS-006: semantic relationships of fixture <c>dotnet-advanced</c> (EXPECTED.md §9) and the syntactic fallback.</summary>
public sealed class CSharpRelationshipTests
{
    private const string Core = "src/Advanced.Core";
    private const string Services = "src/Advanced.Services";

    private static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c6da"));
    private static readonly AnalysisRunId Run = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000045"));

    /// <summary>EXPECTED.md §9 (minimum set): source key, kind, target key, evidence location.</summary>
    private static readonly (string Source, RelationshipKind Kind, string Target, string Evidence)[] Expected =
    [
        ("T:Advanced.Core.Entities.Customer", RelationshipKind.Inherits, "T:Advanced.Core.Entities.Entity`1", $"{Core}/Entities/Customer.cs:5:33"),
        ("T:Advanced.Core.Entities.Customer", RelationshipKind.Implements, "T:Advanced.Core.Abstractions.IAuditable", $"{Core}/Entities/Customer.cs:5:47"),
        ("M:Advanced.Core.Entities.Customer.#ctor(System.Guid,System.String)", RelationshipKind.Calls, "M:Advanced.Core.Entities.Entity`1.#ctor(`0)", $"{Core}/Entities/Customer.cs:8:11"),
        ("M:Advanced.Core.Entities.Customer.AddOrder(System.Decimal)", RelationshipKind.Instantiates, "T:Advanced.Core.Entities.Customer.OrderSummary", $"{Core}/Entities/Customer.Orders.cs:11:25"),
        ("F:Advanced.Core.Entities.Customer._orders", RelationshipKind.UsesType, "T:Advanced.Core.Entities.Customer.OrderSummary", $"{Core}/Entities/Customer.Orders.cs:5:27"),
        ("T:Advanced.Core.Abstractions.IRepository`1", RelationshipKind.UsesType, "T:Advanced.Core.Entities.Entity`1", $"{Core}/Abstractions/IRepository.cs:6:15"),
        ("P:Advanced.Core.Abstractions.Result`1.Kind", RelationshipKind.UsesType, "T:Advanced.Core.Abstractions.ResultKind", $"{Core}/Abstractions/Result.cs:17:12"),
        ("T:Advanced.Core.Abstractions.ResultHandler`1", RelationshipKind.UsesType, "T:Advanced.Core.Abstractions.Result`1", $"{Core}/Abstractions/Result.cs:24:39"),
        ("T:Advanced.Services.Customers.CustomerService", RelationshipKind.Implements, "T:Advanced.Services.Customers.ICustomerService", $"{Services}/Customers/CustomerService.cs:6:41"),
        ("F:Advanced.Services.Customers.CustomerService._repository", RelationshipKind.UsesType, "T:Advanced.Core.Abstractions.IRepository`1", $"{Services}/Customers/CustomerService.cs:8:22"),
        ("F:Advanced.Services.Customers.CustomerService._repository", RelationshipKind.UsesType, "T:Advanced.Core.Entities.Customer", $"{Services}/Customers/CustomerService.cs:8:34"),
        ("M:Advanced.Services.Customers.CustomerService.Create(System.String)", RelationshipKind.Instantiates, "T:Advanced.Core.Entities.Customer", $"{Services}/Customers/CustomerService.cs:17:28"),
        ("M:Advanced.Services.Customers.CustomerService.Create(System.String)", RelationshipKind.Calls, "M:Advanced.Core.Abstractions.IRepository`1.Add(`0)", $"{Services}/Customers/CustomerService.cs:18:21"),
        ("M:Advanced.Services.Customers.CustomerService.Create(System.String)", RelationshipKind.Calls, "M:Advanced.Core.Abstractions.Result`1.Ok(`0)", $"{Services}/Customers/CustomerService.cs:19:33"),
        ("M:Advanced.Services.Customers.ICustomerService.Create(System.String)", RelationshipKind.UsesType, "T:Advanced.Core.Abstractions.Result`1", $"{Services}/Customers/ICustomerService.cs:8:5"),
        ("T:Advanced.Services.Customers.InMemoryCustomerRepository", RelationshipKind.Implements, "T:Advanced.Core.Abstractions.IRepository`1", $"{Services}/Customers/InMemoryCustomerRepository.cs:6:52"),
    ];

    private static async Task<RepositoryAnalysis> AnalyzeAsync(string root, CSharpAnalyzer? analyzer = null)
    {
        var reader = new RepositoryFileReader();
        var pipeline = new AnalysisPipeline(
            new RepositoryScanner(reader, TimeProvider.System), reader, [new DotNetProjectSystemAnalyzer()], [], TimeProvider.System, [analyzer ?? new CSharpAnalyzer()]);
        var result = await pipeline.AnalyzeAsync(new AnalysisRequest(Repository, new RepositoryRoot(root), Run), null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private static string Key(ArchitectureModel model, ElementReference reference) => model.FindEntity(reference.Id)!.SymbolKey;

    private static string Location(ArchitectureModel model, AnalysisEvidence evidence) =>
        $"{model.FindFile(evidence.Location!.Value.FileId)!.Path}:{evidence.Location.Value.StartLine}:{evidence.Location.Value.StartColumn}";

    [Fact]
    public async Task Relationships_ContainTheMinimumSetOfExpectedSection9_AllConfirmed()
    {
        var model = (await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("dotnet-advanced"))).Model;
        var code = model.Relationships.Where(r => r.Source.Kind == ElementKind.Entity).ToArray();

        Assert.All(Expected, expected =>
        {
            var relationship = Assert.Single(code, r => r.Kind == expected.Kind && Key(model, r.Source) == expected.Source && Key(model, r.Target) == expected.Target);
            Assert.Equal(EvidenceLevel.Confirmed, relationship.Level);
            var evidence = Assert.Single(relationship.Evidence, e => Location(model, e) == expected.Evidence);
            Assert.Equal(CSharpCodes.AnalyzerName, evidence.Analyzer.Value);
            Assert.Equal($"csharp.{expected.Kind}", evidence.Rule);
            Assert.Null(evidence.Excerpt);
        });
    }

    [Fact]
    public async Task Relationships_MustNotExist()
    {
        var model = (await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("dotnet-advanced"))).Model;
        var code = model.Relationships.Where(r => r.Source.Kind == ElementKind.Entity).ToArray();

        // Property access is not a call; no evidence from the comment or the string in lines 16–17.
        Assert.DoesNotContain(code, r => Key(model, r.Target) == "P:Advanced.Core.Entities.Entity`1.Id");
        Assert.DoesNotContain(code.SelectMany(r => r.Evidence), e =>
            Location(model, e).StartsWith($"{Services}/ServiceCollectionExtensions.cs:16:", StringComparison.Ordinal)
            || Location(model, e).StartsWith($"{Services}/ServiceCollectionExtensions.cs:17:", StringComparison.Ordinal));
        Assert.All(code, r => Assert.True(r.Kind is RelationshipKind.Inherits or RelationshipKind.Implements or RelationshipKind.UsesType
            or RelationshipKind.Instantiates or RelationshipKind.Calls, r.Kind.ToString()));
        Assert.All(code, r => Assert.NotEqual(r.Source.Id, r.Target.Id));
    }

    [Fact]
    public async Task CrossProjectRelationships_ConnectEntitiesOfDifferentProjects()
    {
        var model = (await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("dotnet-advanced"))).Model;

        var crossProject = model.Relationships
            .Where(r => r.Source.Kind == ElementKind.Entity)
            .Where(r => model.FindEntity(r.Source.Id)!.ProjectId != model.FindEntity(r.Target.Id)!.ProjectId)
            .ToArray();

        Assert.NotEmpty(crossProject);
        Assert.All(crossProject, r => Assert.Equal("Advanced.Core", model.FindProject(model.FindEntity(r.Target.Id)!.ProjectId)!.Name));
    }

    [Fact]
    public async Task UnresolvedTypeWithAUniqueName_IsSyntactic()
    {
        // Two projects without a project reference: App uses Lib's type by name only.
        using var repository = new TempDirectory();
        repository.WriteText("Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        repository.WriteText("Lib/Widget.cs", "namespace Lib;\npublic class Widget { }\n");
        repository.WriteText("App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        repository.WriteText("App/Shop.cs", "using Lib;\nnamespace App;\npublic class Shop\n{\n    private Widget? _widget;\n}\n");

        var analysis = await AnalyzeAsync(repository.FullPath);
        var model = analysis.Model;

        var usage = Assert.Single(model.Relationships, r => r.Kind == RelationshipKind.UsesType);
        Assert.Equal("F:App.Shop._widget", Key(model, usage.Source));
        Assert.Equal("T:Lib.Widget", Key(model, usage.Target));
        Assert.Equal(EvidenceLevel.Syntactic, usage.Level);
        Assert.Equal("App/Shop.cs:5:13", Location(model, Assert.Single(usage.Evidence)));
    }

    [Fact]
    public void Compilations_AreReleasedAfterTheRun()
    {
        var created = AnalyzeAndTrackCompilations();

        Assert.Equal(2, created.Count);
        for (var attempt = 0; attempt < 10 && created.Any(r => r.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.DoesNotContain(created, r => r.IsAlive);
    }

    /// <summary>
    /// Runs the analysis in its own, non-inlined frame on the thread pool: an awaiting async test method would
    /// itself keep the completed analysis tasks (and thus their state) reachable, which says nothing about the
    /// analyzer.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static List<WeakReference> AnalyzeAndTrackCompilations()
    {
        var created = new List<WeakReference>();
        var analyzer = new CSharpAnalyzer { CompilationCreated = c => created.Add(new WeakReference(c)) };
        var root = TestRepositoryCatalog.GetFixturePath("dotnet-advanced");

        Task.Run(() => AnalyzeAsync(root, analyzer)).GetAwaiter().GetResult();
        return created;
    }
}
