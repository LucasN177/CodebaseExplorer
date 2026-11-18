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
    private readonly string _root;
    private readonly bool _ownsRoot;

    public TestApplication()
        : this(Path.Combine(Path.GetTempPath(), "codebase-explorer-integration-tests", Guid.NewGuid().ToString("N")), ownsRoot: true)
    {
    }

    private TestApplication(string root, bool ownsRoot)
    {
        _root = root;
        _ownsRoot = ownsRoot;
        Services = CompositionRoot.BuildServiceProvider(services =>
            services.AddSingleton<IApplicationPaths>(new ApplicationPaths(Path.Combine(_root, "data"))));
    }

    public ServiceProvider Services { get; }

    /// <summary>
    /// A second application instance on the same data directory, like a restart of the app. Dispose it before
    /// this instance; only this instance deletes the directory.
    /// </summary>
    public TestApplication Restart() => new(_root, ownsRoot: false);

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
        if (!_ownsRoot)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        try
        {
            // Clones (GIT-003) contain Git's read-only object files, which block deletion on Windows.
            if (Directory.Exists(_root))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(_root, "*", options))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a lingering handle must not fail the test.
        }
    }
}
