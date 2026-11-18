using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Security;

/// <summary>
/// ANALYSIS-009 with the QA-003 harness: importing fixture <c>node-hostile</c> through the real composition root
/// runs no package script, no node-gyp action and loads no module (SECURITY.md §6).
/// </summary>
public sealed class HostileNodeFixtureTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly TempDirectory _repository = ZeroExecutionHarness.CopyFixture("node-hostile");

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
        var model = stored!.Model;

        var root = model.Projects.Single(p => p.Name == "node-hostile");
        Assert.Equal(ProjectKind.NodeWorkspaceRoot, root.Kind);
        Assert.Equal("install;postinstall;preinstall;prepare", root.Properties["lifecycleScripts"]);
        Assert.Equal("install", model.Projects.Single(p => p.Name == "native-addon").Properties["lifecycleScripts"]);
        Assert.Equal("postinstall", model.Projects.Single(p => p.Name == "evil-local").Properties["lifecycleScripts"]);
        Assert.Equal(
            ["evil-local", "native-addon"],
            model.Relationships.Where(r => r.Kind == RelationshipKind.ProjectReference && r.Source.Id == root.Id)
                .Select(r => model.FindProject(r.Target.Id)!.Name).Order(StringComparer.Ordinal));
        Assert.Contains(model.ExternalDependencies, d => d.Name == "evil-git");

        // Nothing ran: the copy is byte-identical, no marker anywhere a payload could write to.
        Assert.Empty(ZeroExecutionHarness.Changes(_repository.FullPath, before));
        Assert.Empty(ZeroExecutionHarness.Markers(_repository.FullPath));
        Assert.Equal(outputBefore, ZeroExecutionHarness.Markers(AppContext.BaseDirectory));
        Assert.Empty(ZeroExecutionHarness.Markers(_app.Services.GetRequiredService<IApplicationPaths>().DataDirectory));
    }
}
