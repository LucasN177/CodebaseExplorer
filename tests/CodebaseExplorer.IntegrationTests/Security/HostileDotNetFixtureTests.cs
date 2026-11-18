using System.Reflection;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Security;

/// <summary>
/// QA-003: the full import pipeline (real composition root) over fixture <c>dotnet-hostile</c> executes nothing —
/// no MSBuild target, task or property function, no analyzer or source generator, no module initializer — and the
/// modules that read repository content cannot start processes (SECURITY.md §6).
/// </summary>
public sealed class HostileDotNetFixtureTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly TempDirectory _repository = ZeroExecutionHarness.CopyFixture("dotnet-hostile");

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

        // The repository is analyzed like any other: the project and its code are found statically.
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        var stored = await _app.Services.GetRequiredService<IArchitectureModelStore>()
            .LoadCurrentModelAsync(new Domain.Common.RepositoryId(Guid.ParseExact(result.Value.RepositoryId, "N")), TestContext.Current.CancellationToken);
        Assert.Contains(stored!.Model.Projects, p => p.Name == "Hostile.App");
        Assert.Contains(stored.Model.Entities, e => e.SymbolKey == "M:Hostile.App.Startup.WriteMarker");

        // Nothing ran: the copy is byte-identical, no marker anywhere a payload could write to.
        Assert.Empty(ZeroExecutionHarness.Changes(_repository.FullPath, before));
        Assert.Empty(ZeroExecutionHarness.Markers(_repository.FullPath));
        Assert.Equal(outputBefore, ZeroExecutionHarness.Markers(AppContext.BaseDirectory));
        Assert.Empty(ZeroExecutionHarness.Markers(_app.Services.GetRequiredService<CodebaseExplorer.Application.Abstractions.IApplicationPaths>().DataDirectory));

        // No assembly was loaded from the repository (the referenced "generator" is not even a valid image).
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a =>
            !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && a.Location.StartsWith(_repository.FullPath, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("CodebaseExplorer.Analysis")]
    [InlineData("CodebaseExplorer.Storage")]
    [InlineData("CodebaseExplorer.Git")]
    [InlineData("CodebaseExplorer.AI")]
    [InlineData("CodebaseExplorer.Graph")]
    [InlineData("CodebaseExplorer.Application")]
    [InlineData("CodebaseExplorer.Domain")]
    [InlineData("CodebaseExplorer.Contracts")]
    public void Module_CannotStartProcesses(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        Assert.Empty(ZeroExecutionHarness.ProcessApiUsages(assembly));
    }

    [Fact]
    public void Harness_DetectsMarkersAndChanges()
    {
        // Guard against a harness that can never fail.
        var before = ZeroExecutionHarness.Snapshot(_repository.FullPath);
        File.WriteAllText(Path.Combine(_repository.FullPath, "src", ZeroExecutionHarness.MarkerPrefix + "probe.txt"), "executed");
        File.AppendAllText(Path.Combine(_repository.FullPath, "Directory.Build.props"), "\n");

        Assert.Single(ZeroExecutionHarness.Markers(_repository.FullPath));
        Assert.Equal(2, ZeroExecutionHarness.Changes(_repository.FullPath, before).Count);
        // This test assembly references Process (below), so the IL guard must report it.
        Assert.True(CurrentProcessId() > 0);
        Assert.Contains("type reference System.Diagnostics.Process", ZeroExecutionHarness.ProcessApiUsages(typeof(HostileDotNetFixtureTests).Assembly));
    }

    private static int CurrentProcessId() => System.Diagnostics.Process.GetCurrentProcess().Id;
}
