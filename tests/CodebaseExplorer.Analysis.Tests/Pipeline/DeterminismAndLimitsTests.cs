using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodebaseExplorer.Analysis.Languages.TreeSitter;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Analysis.Tests.Pipeline;

/// <summary>
/// ANALYSIS-013: every fixture analyzed twice (with different parallelism) yields byte-identical serialized models and
/// diagnostics; the parse-time limit is enforced with a diagnostic; statistics include per-analyzer durations.
/// </summary>
public sealed class DeterminismAndLimitsTests : IDisposable
{
    private static readonly RepositoryId Repository = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-8293a4b5c9c5"));
    private static readonly AnalysisRunId Run = new(Guid.Parse("0193a1b2-c3d4-7e5f-8071-000000000141"));

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new ContentHashConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ServiceProvider _services = new ServiceCollection()
        .AddSingleton(TimeProvider.System)
        .AddAnalysisModule()
        .BuildServiceProvider();

    private readonly TempDirectory _repository = new();

    public void Dispose()
    {
        _services.Dispose();
        _repository.Dispose();
    }

    public static TheoryData<string> AllFixtures() =>
        [.. Directory.GetDirectories(TestRepositoryCatalog.FixturesRoot).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    private async Task<RepositoryAnalysis> AnalyzeAsync(string root, AnalysisLimits limits)
    {
        var result = await _services.GetRequiredService<IRepositoryAnalyzer>().AnalyzeAsync(
            new AnalysisRequest(Repository, new RepositoryRoot(root), Run, limits), null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    /// <summary>Canonical serialization of everything the model and the diagnostics contain (all public properties).</summary>
    private static string Serialize(ArchitectureModel model, IReadOnlyList<AnalysisDiagnostic> diagnostics) =>
        JsonSerializer.Serialize(new { model, diagnostics }, Json);

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task Fixture_AnalyzedTwice_YieldsByteIdenticalModels(string fixture)
    {
        var root = TestRepositoryCatalog.GetFixturePath(fixture);

        var sequential = await AnalyzeAsync(root, AnalysisLimits.Default with { MaxDegreeOfParallelism = 1 });
        var parallel = await AnalyzeAsync(root, AnalysisLimits.Default with { MaxDegreeOfParallelism = 8 });

        var first = Serialize(sequential.Model, sequential.Diagnostics);
        Assert.Contains($"\"{sequential.Model.Files[^1].Path.Value}\"", first, StringComparison.Ordinal); // the serialization is not empty
        Assert.All(sequential.Model.Relationships, r => Assert.Contains(r.NaturalKey.Value, first, StringComparison.Ordinal));
        Assert.Equal(first, Serialize(parallel.Model, parallel.Diagnostics));
        Assert.Equal(sequential.AnalyzerVersions, parallel.AnalyzerVersions);
    }

    [Fact]
    public async Task ParseTimeLimit_SkipsFilesWithAWarning_ButKeepsTheirModules()
    {
        _repository.WriteText("package.json", "{ \"name\": \"p\" }");
        _repository.WriteText("a.ts", "import { b } from './b';\nexport class A {}");
        _repository.WriteText("b.ts", "export const b = 1;");
        _repository.WriteText("tool.py", "import os\nclass Tool: ...");

        var analysis = await AnalyzeAsync(_repository.FullPath, AnalysisLimits.Default with { MaxParseDuration = TimeSpan.Zero });

        Assert.Equal(3, analysis.Diagnostics.Count(d => d is { Code: TreeSitterParser.ParseTimeoutCode, Severity: DiagnosticSeverity.Warning }));
        Assert.Empty(analysis.Model.Entities);
        Assert.Equal(["a.ts", "b.ts", "tool"], analysis.Model.Modules.Select(m => m.QualifiedName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Statistics_IncludeStageAndAnalyzerDurations()
    {
        var analysis = await AnalyzeAsync(TestRepositoryCatalog.GetFixturePath("mixed-polyglot"), AnalysisLimits.Default);

        Assert.Equal(
            [
                "analyzer.csharp.roslyn", "analyzer.dotnet.msbuild-static", "analyzer.node.manifest", "analyzer.python.manifest",
                "analyzer.treesitter.python", "analyzer.treesitter.typescript", "languages", "link", "project-systems", "scan", "validate",
            ],
            analysis.Statistics.StageDurations.Keys);
        Assert.All(analysis.Statistics.StageDurations.Values, d => Assert.True(d >= TimeSpan.Zero));
    }

    /// <summary>Writes a content hash as its hex text (its byte span cannot be serialized directly).</summary>
    private sealed class ContentHashConverter : JsonConverter<ContentHash>
    {
        public override ContentHash Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, ContentHash value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
