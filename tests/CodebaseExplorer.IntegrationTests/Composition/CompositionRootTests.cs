using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Desktop.Composition;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Composition;

public sealed class CompositionRootTests
{
    [Fact]
    public void BuildServiceProvider_ValidatesAllRegistrations()
    {
        // ValidateOnBuild = true makes BuildServiceProvider throw for any unresolvable registration.
        using var provider = CompositionRoot.BuildServiceProvider();

        Assert.NotNull(provider);
    }

    [Fact]
    public void BuildServiceProvider_ResolvesShellViewModelWithAllPages()
    {
        using var provider = CompositionRoot.BuildServiceProvider();

        var shell = provider.GetRequiredService<MainWindowViewModel>();

        Assert.Equal(["Start", "Architecture Explorer", "Settings"], shell.Pages.Select(p => p.Title));
        Assert.Same(shell.Pages[0], shell.CurrentPage);
    }

    [Fact]
    public void BuildServiceProvider_RegistersOneGraphViewBridgeAsGraphViewHost()
    {
        using var provider = CompositionRoot.BuildServiceProvider();

        var host = provider.GetRequiredService<IGraphViewHost>();

        Assert.Same(provider.GetRequiredService<GraphViewBridge>(), host);
    }

    [Fact]
    public void SuccessfulImport_NavigatesTheShellToTheExplorer()
    {
        using var provider = CompositionRoot.BuildServiceProvider();
        var shell = provider.GetRequiredService<MainWindowViewModel>();

        provider.GetRequiredService<RepositorySession>().SetCurrent(new Contracts.Repositories.RepositoryImportSummaryDto
        {
            RepositoryId = new string('a', 32),
            Name = "repo",
            RootPath = Path.GetTempPath(),
            AnalysisRunId = new string('b', 32),
            Status = "succeeded",
        });

        Assert.Equal("Architecture Explorer", shell.CurrentPage.Title);
    }

    [Fact]
    public void BuildServiceProvider_RegistersApplicationPaths()
    {
        using var provider = CompositionRoot.BuildServiceProvider();

        var paths = provider.GetRequiredService<IApplicationPaths>();

        Assert.True(Path.IsPathFullyQualified(paths.DataDirectory));
    }
}
