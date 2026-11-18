using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Inspector;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Model;
using CodebaseExplorer.Domain.Runs;
using CodebaseExplorer.IntegrationTests.Security;
using CodebaseExplorer.TestRepositories;
using CodebaseExplorer.TestRepositories.Git;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Git;

/// <summary>
/// GIT-005 end to end through the real composition root and SQLite: import of a builder-created Git repository stores
/// the snapshot and per-file statistics, and the graph overlay and the inspector show exactly the persisted numbers.
/// </summary>
public sealed class GitActivityEndToEndTests : IAsyncLifetime
{
    private const string ChangedFile = "src/Shop.Domain/Orders/Order.cs";

    private readonly TestApplication _app = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Import_StoresSnapshotAndStatistics_AndTheRunRecordsHead()
    {
        using var built = LayeredHistory().Build();

        var summary = await ImportAsync(built.WorkingDirectory);
        var git = await Get<IGitActivityStore>().LoadCurrentAsync(IdOf(summary), Ct);
        var stored = await Get<IArchitectureModelStore>().LoadCurrentModelAsync(IdOf(summary), Ct);

        Assert.NotNull(git);
        Assert.Equal(built.Commits[^1], git.Snapshot.HeadCommitSha);
        Assert.Equal(GitRepositoryBuilder.DefaultBranch, git.Snapshot.Branch);
        Assert.False(git.Snapshot.HasUncommittedChanges);
        Assert.Equal([new GitRemoteInfo("origin", "https://github.com/example/shop.git")], git.Snapshot.Remotes);
        Assert.Equal(2, git.Snapshot.HistoryDepth);
        Assert.Equal(GitRepositoryBuilder.DefaultStart, git.Snapshot.HistoryWindowStart);
        Assert.Equal(built.Commits[^1], stored!.Run.CommitSha);
        Assert.Equal(GitRepositoryBuilder.DefaultBranch, stored.Run.Branch);

        var changed = git.Files[FileId(stored.Model, ChangedFile)];
        Assert.Equal(new GitFileChanges(2, 2, changed.LinesAdded, changed.LinesDeleted, GitRepositoryBuilder.DefaultStart,
            GitRepositoryBuilder.DefaultStart.AddHours(1), built.Commits[^1]), changed);
        Assert.Equal(1, git.Files[FileId(stored.Model, "src/Shop.Web/Program.cs")].CommitCount);
        Assert.All(git.Files.Keys, id => Assert.NotNull(stored.Model.FindFile(id)));
    }

    [Fact]
    public async Task Overlay_MatchesThePersistedNumbers_ForProjectsModulesAndTypes()
    {
        using var built = LayeredHistory().Build();
        var summary = await ImportAsync(built.WorkingDirectory);
        var model = (await Get<IArchitectureModelStore>().LoadCurrentModelAsync(IdOf(summary), Ct))!.Model;
        var git = (await Get<IGitActivityStore>().LoadCurrentAsync(IdOf(summary), Ct))!;
        var domain = model.Projects.Single(p => p.Name == "Shop.Domain");
        var expanded = model.Modules.Where(m => m.ProjectId == domain.Id).Select(m => m.Id.ToString()).Append(domain.Id.ToString()).ToList();

        var view = (await Get<GetGraphView>().ExecuteAsync(
            new GraphQuery { RepositoryId = summary.RepositoryId, IncludeActivity = true, Expanded = expanded }, Ct)).Value;
        var plain = (await Get<GetGraphView>().ExecuteAsync(new GraphQuery { RepositoryId = summary.RepositoryId, Expanded = expanded }, Ct)).Value;

        // Independent recomputation from the persisted rows: projects sum their own files.
        foreach (var project in model.Projects)
        {
            var expected = model.Files.Where(f => f.ProjectId == project.Id).Sum(f => git.Files.TryGetValue(f.Id, out var c) ? c.CommitCount : 0);
            Assert.Equal(expected, view.Nodes.Single(n => n.Id == project.Id.ToString()).Metrics.ChangeCount);
        }

        Assert.Equal(model.Files.Count(f => f.ProjectId == domain.Id) + 1, view.Nodes.Single(n => n.Id == domain.Id.ToString()).Metrics.ChangeCount);
        var order = model.Entities.Single(e => e.Name == "Order" && e.ContainingEntityId is null);
        Assert.Equal(2, view.Nodes.Single(n => n.Id == order.Id.ToString()).Metrics.ChangeCount);
        Assert.Equal(1, view.Nodes.Single(n => n.Id == model.Entities.Single(e => e.Name == "OrderLine" && e.ContainingEntityId is null).Id.ToString()).Metrics.ChangeCount);
        Assert.Equal(4, view.Nodes.Single(n => n.Id == order.ModuleId.ToString()).Metrics.ChangeCount);
        Assert.All(plain.Nodes, n => Assert.Null(n.Metrics.ChangeCount));

        var details = (await Get<GetElementDetails>().ExecuteAsync(summary.RepositoryId, domain.Id.ToString(), Ct)).Value;
        Assert.NotNull(details.Activity);
        Assert.Equal(view.Nodes.Single(n => n.Id == domain.Id.ToString()).Metrics.ChangeCount, details.Activity.FileChanges);
        Assert.Equal(model.Files.Count(f => f.ProjectId == domain.Id), details.Activity.ChangedFiles);
        Assert.Equal(GitRepositoryBuilder.DefaultStart.AddHours(1), details.Activity.LastChangeAt);
        Assert.Equal(built.Commits[^1], details.Activity.HeadCommitSha);
        Assert.Null(details.Activity.DistinctAuthors);

        var type = (await Get<GetElementDetails>().ExecuteAsync(summary.RepositoryId, order.Id.ToString(), Ct)).Value;
        Assert.Equal((2, 1, 2), (type.Activity!.FileChanges, type.Activity.ChangedFiles, type.Activity.DistinctAuthors));
    }

    [Fact]
    public async Task RepositoryWithoutGit_WorksUnchanged()
    {
        using var folder = ZeroExecutionHarness.CopyFixture("dotnet-layered");

        var summary = await ImportAsync(folder.FullPath);
        var stored = await Get<IArchitectureModelStore>().LoadCurrentModelAsync(IdOf(summary), Ct);
        var view = (await Get<GetGraphView>().ExecuteAsync(new GraphQuery { RepositoryId = summary.RepositoryId, IncludeActivity = true }, Ct)).Value;
        var details = (await Get<GetElementDetails>().ExecuteAsync(summary.RepositoryId, view.Nodes[0].Id, Ct)).Value;

        Assert.Null(await Get<IGitActivityStore>().LoadCurrentAsync(IdOf(summary), Ct));
        Assert.Null(stored!.Run.CommitSha);
        Assert.DoesNotContain(stored.Run.Diagnostics, d => d.Code.StartsWith("git.", StringComparison.Ordinal));
        Assert.All(view.Nodes, n => Assert.Null(n.Metrics.ChangeCount));
        Assert.Null(details.Activity);
    }

    [Fact]
    public async Task UnreadableRepository_IsImportedWithAWarningAndWithoutGitData()
    {
        using var built = LayeredHistory().AppendConfig("[core\n\tbroken = = =").Build();

        var summary = await ImportAsync(built.WorkingDirectory);
        var stored = await Get<IArchitectureModelStore>().LoadCurrentModelAsync(IdOf(summary), Ct);

        Assert.Contains(stored!.Run.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning && d.Code == GitErrorCodes.ReadFailed);
        Assert.Null(await Get<IGitActivityStore>().LoadCurrentAsync(IdOf(summary), Ct));
        Assert.True(stored.Model.Projects.Count > 0);
    }

    [Fact]
    public async Task Reimport_ReplacesTheStatistics_AndRemoveDeletesThem()
    {
        using var built = LayeredHistory().Build();
        var first = await ImportAsync(built.WorkingDirectory);
        File.AppendAllText(built.PathOf(ChangedFile), "// third change\n");
        using (var repository = new LibGit2Sharp.Repository(built.WorkingDirectory))
        {
            LibGit2Sharp.Commands.Stage(repository, "*");
            var signature = new LibGit2Sharp.Signature("Linus", "linus@example.com", GitRepositoryBuilder.DefaultStart.AddHours(2));
            repository.Commit("third", signature, signature);
        }

        var second = await ImportAsync(built.WorkingDirectory);
        var model = (await Get<IArchitectureModelStore>().LoadCurrentModelAsync(IdOf(second), Ct))!.Model;
        var git = (await Get<IGitActivityStore>().LoadCurrentAsync(IdOf(second), Ct))!;

        Assert.Equal(first.RepositoryId, second.RepositoryId);
        Assert.Equal(3, git.Snapshot.HistoryDepth);
        Assert.Equal((3, 3), (git.Files[FileId(model, ChangedFile)].CommitCount, git.Files[FileId(model, ChangedFile)].DistinctAuthorCount));

        Assert.True((await Get<RemoveRepository>().ExecuteAsync(second.RepositoryId, Ct)).IsSuccess);
        Assert.Null(await Get<IGitActivityStore>().LoadCurrentAsync(IdOf(second), Ct));
        Assert.True(Directory.Exists(built.WorkingDirectory), "A local repository is never deleted.");
    }

    /// <summary>The dotnet-layered fixture committed by Ada, then Order.cs changed by Grace.</summary>
    private static GitRepositoryBuilder LayeredHistory()
    {
        var builder = GitRepositoryBuilder.Create();
        var source = TestRepositoryCatalog.GetFixturePath("dotnet-layered");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            builder.Write(Path.GetRelativePath(source, file).Replace('\\', '/'), File.ReadAllText(file));
        }

        return builder.Commit("import", GitTestAuthor.Ada)
            .Write(ChangedFile, File.ReadAllText(Path.Combine(source, "src", "Shop.Domain", "Orders", "Order.cs")) + "// changed\n")
            .Commit("change order", GitTestAuthor.Grace)
            .Remote("origin", "https://user:secret@github.com/example/shop.git");
    }

    private async Task<RepositoryImportSummaryDto> ImportAsync(string path)
    {
        var result = await Get<ImportLocalRepository>().ExecuteAsync(path, null, Ct);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        return result.Value;
    }

    private static RepositoryId IdOf(RepositoryImportSummaryDto summary) => new(Guid.ParseExact(summary.RepositoryId, "N"));

    private static ElementId FileId(ArchitectureModel model, string path) => model.Files.Single(f => f.Path.Value == path).Id;

    private T Get<T>()
        where T : notnull => _app.Services.GetRequiredService<T>();
}
