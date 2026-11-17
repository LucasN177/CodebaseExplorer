using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Desktop.Composition;
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
    public void BuildServiceProvider_RegistersApplicationPaths()
    {
        using var provider = CompositionRoot.BuildServiceProvider();

        var paths = provider.GetRequiredService<IApplicationPaths>();

        Assert.True(Path.IsPathFullyQualified(paths.DataDirectory));
    }
}
