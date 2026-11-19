using CodebaseExplorer.Desktop.Converters;
using CodebaseExplorer.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>UI modernization: every navigation page has a vector icon.</summary>
public sealed class AppIconsTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    // Geometries need Avalonia's render platform, which the tests do not start; the icons were checked in the running app.

    [Fact]
    public void EveryPage_HasAKnownIcon()
    {
        var shell = _app.Services.GetRequiredService<MainWindowViewModel>();

        Assert.All(shell.Pages, page => Assert.Contains(page.IconKey, AppIcons.Keys));
    }
}
