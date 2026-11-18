using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.Languages.CSharp;
using CodebaseExplorer.Analysis.Languages.Python;
using CodebaseExplorer.Analysis.Languages.TypeScript;
using CodebaseExplorer.Analysis.Pipeline;
using CodebaseExplorer.Analysis.ProjectSystems.DotNet;
using CodebaseExplorer.Analysis.ProjectSystems.Node;
using CodebaseExplorer.Analysis.ProjectSystems.Python;
using CodebaseExplorer.Analysis.Scanning;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Analysis;

/// <summary>
/// Registration entry point of the Analysis module. Hosts repository file access, the analyzer
/// pipeline and the language analyzers (ANALYSIS_ENGINE.md).
/// </summary>
public static class DependencyInjection
{
    /// <summary>Registers the services of the Analysis module.</summary>
    public static IServiceCollection AddAnalysisModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRepositoryRootValidator, RepositoryRootValidator>();
        services.AddSingleton<IRepositoryFileReader>(_ => new RepositoryFileReader(SymbolicLinkPolicy.DoNotFollow));

        services.AddSingleton<RepositoryScanner>();
        services.AddSingleton<IRepositoryAnalyzer, AnalysisPipeline>();

        // Project-system analyzers (stage 2) and language analyzers (stage 3).
        services.AddSingleton<IProjectSystemAnalyzer, DotNetProjectSystemAnalyzer>();
        services.AddSingleton<IProjectSystemAnalyzer, NodeProjectSystemAnalyzer>();
        services.AddSingleton<IProjectSystemAnalyzer, PythonProjectSystemAnalyzer>();
        services.AddSingleton<IRepositoryLanguageAnalyzer, CSharpAnalyzer>();
        services.AddSingleton<ILanguageAnalyzer, TypeScriptAnalyzer>();
        services.AddSingleton<ILanguageAnalyzer, PythonAnalyzer>();

        return services;
    }
}
