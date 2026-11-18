using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Security;

/// <summary>
/// ANALYSIS-011 with the QA-003 harness: importing fixture <c>python-hostile</c> through the real composition root
/// runs no <c>setup.py</c>, build backend, pytest hook, site customization or package code (SECURITY.md §6).
/// </summary>
public sealed class HostilePythonFixtureTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly TempDirectory _repository = ZeroExecutionHarness.CopyFixture("python-hostile");

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _repository.Dispose();
    }

    [Fact]
    public async Task Import_ExecutesNoPayload()
    {
        var before = ZeroExecutionHarness.Snapshot(_repository.FullPath);
        var outputBefore = ZeroExecutionHarness.Markers(AppContext.BaseDirectory);

        var result = await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(_repository.FullPath, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        var stored = await _app.Services.GetRequiredService<IArchitectureModelStore>()
            .LoadCurrentModelAsync(new RepositoryId(Guid.ParseExact(result.Value.RepositoryId, "N")), TestContext.Current.CancellationToken);
        var project = Assert.Single(stored!.Model.Projects);
        Assert.Equal(("python-hostile", "evil_backend", "true"), (project.Name, project.Properties["buildBackend"], project.Properties["setupPy"]));
        Assert.Equal(["click", "evilpkg", "requests"], stored.Model.ExternalDependencies.Select(d => d.Name).Order(StringComparer.Ordinal));

        // Nothing ran: the copy is byte-identical, no marker anywhere a payload could write to.
        Assert.Empty(ZeroExecutionHarness.Changes(_repository.FullPath, before));
        Assert.Empty(ZeroExecutionHarness.Markers(_repository.FullPath));
        Assert.Equal(outputBefore, ZeroExecutionHarness.Markers(AppContext.BaseDirectory));
        Assert.Empty(ZeroExecutionHarness.Markers(_app.Services.GetRequiredService<IApplicationPaths>().DataDirectory));
    }
}
