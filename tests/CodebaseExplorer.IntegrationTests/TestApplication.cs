using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Desktop.Composition;
using CodebaseExplorer.Infrastructure.FileSystem;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests;

/// <summary>
/// The real composition root, pointed at a temporary data directory with a migrated database, so tests
/// never touch the user's data. Call <see cref="InitializeAsync"/> before use (e.g. from <c>IAsyncLifetime</c>).
/// </summary>
internal sealed class TestApplication : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "codebase-explorer-integration-tests", Guid.NewGuid().ToString("N"));

    public TestApplication() =>
        Services = CompositionRoot.BuildServiceProvider(services =>
            services.AddSingleton<IApplicationPaths>(new ApplicationPaths(Path.Combine(_root, "data"))));

    public ServiceProvider Services { get; }

    /// <summary>Creates and migrates the database exactly like the application does at startup.</summary>
    public async ValueTask InitializeAsync()
    {
        var result = await Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Test database could not be prepared: {result.Error}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a lingering handle must not fail the test.
        }
    }
}
