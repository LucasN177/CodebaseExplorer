using CodebaseExplorer.Analysis.Languages.CSharp;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.Analysis.Tests.Languages.CSharp;

/// <summary>ANALYSIS-005: compilation setup from static properties; references never come from the machine or the repository.</summary>
public sealed class CompilationSettingsTests
{
    [Fact]
    public void FrameworkReferences_AreEmbedded_NotReadFromDisk()
    {
        // Basic.Reference.Assemblies creates in-memory references: no file path, identical on every machine.
        Assert.NotEmpty(CompilationSettings.FrameworkReferences);
        Assert.All(CompilationSettings.FrameworkReferences, r => Assert.True(string.IsNullOrEmpty(r.FilePath) || !Path.IsPathRooted(r.FilePath), r.FilePath));
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("probe", references: CompilationSettings.FrameworkReferences);
        Assert.NotNull(compilation.GetTypeByMetadataName("System.Guid"));
        Assert.NotNull(compilation.GetTypeByMetadataName("System.Collections.Generic.List`1"));
    }

    [Theory]
    [InlineData("enable", "Microsoft.NET.Sdk", true, false)]
    [InlineData("true", "Microsoft.NET.Sdk.Web", true, true)]
    [InlineData("disable", "Microsoft.NET.Sdk", false, false)]
    [InlineData(null, "Microsoft.NET.Sdk", false, false)]
    public void ImplicitUsings_FollowTheSdkDefaults(string? implicitUsings, string sdk, bool expected, bool web)
    {
        var properties = new Dictionary<string, string> { ["Sdk"] = sdk };
        if (implicitUsings is not null)
        {
            properties["ImplicitUsings"] = implicitUsings;
        }

        var source = CompilationSettings.ImplicitUsingsSource(properties);

        Assert.Equal(expected, source is not null);
        if (source is not null)
        {
            Assert.Contains("global using global::System.Collections.Generic;", source, StringComparison.Ordinal);
            Assert.Equal(web, source.Contains("Microsoft.AspNetCore.Http", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ParseOptions_UseDefineConstantsAndNetSymbols_ButNoUnresolvedProperties()
    {
        var options = CompilationSettings.ParseOptions(new Dictionary<string, string> { ["DefineConstants"] = "TRACE; FEATURE_X;$(Extra)" });

        Assert.Equal(["TRACE", "FEATURE_X", "NET", "NETCOREAPP"], options.PreprocessorSymbolNames);
    }

    [Fact]
    public async Task Analysis_LoadsNoAssemblyFromTheRepository()
    {
        var root = TestRepositoryCatalog.FixturesRoot;

        await CSharpAnalyzerFixtureTests.AnalyzeAsync("dotnet-advanced");

        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a =>
            !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && a.Location.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }
}
