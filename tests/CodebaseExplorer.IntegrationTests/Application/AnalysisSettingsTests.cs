using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Application.Settings;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.IntegrationTests.Security;
using CodebaseExplorer.TestRepositories.Git;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>UI-009: analysis limits, excluded folders and the Git window are stored and applied by the next import.</summary>
public sealed class AnalysisSettingsTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private AnalysisSettingsStore Store => _app.Services.GetRequiredService<AnalysisSettingsStore>();

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public void Defaults_MatchTheBuiltInLimits()
    {
        var now = new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(AnalysisLimits.Default,
            AnalysisSettings.Default.ToLimits() with { ExcludedDirectoryNames = AnalysisLimits.Default.ExcludedDirectoryNames });
        Assert.Equal(AnalysisLimits.DefaultExcludedDirectoryNames, AnalysisSettings.Default.ToLimits().ExcludedDirectoryNames);
        Assert.Equal(GitHistoryWindow.Default(now), AnalysisSettings.Default.ToHistoryWindow(now));
        Assert.Null((AnalysisSettings.Default with { HistoryDays = 0 }).ToHistoryWindow(now).Since);
    }

    [Fact]
    public void ExtraFolders_AreAddedToTheBuiltInOnes()
    {
        var limits = (AnalysisSettings.Default with { ExcludedFolders = ["vendor", "BIN"] }).ToLimits();

        Assert.Contains("vendor", limits.ExcludedDirectoryNames);
        Assert.Single(limits.ExcludedDirectoryNames, n => string.Equals(n, "bin", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("vendor", true)]
    [InlineData("my generated code", true)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("*.cs", false)]
    [InlineData(" padded", false)]
    public void FolderNames_AreChecked(string name, bool valid) => Assert.Equal(valid, AnalysisSettings.IsFolderName(name));

    [Fact]
    public async Task Settings_RoundTrip_AndDamagedValuesFallBackOneByOne()
    {
        var saved = (await Store.SaveAsync(AnalysisSettings.Default with { MaxFiles = 1234, ExcludedFolders = ["vendor"], HistoryMaxCommits = 10 }, Token)).Value;
        Assert.Equal(saved, await Store.LoadAsync(Token) with { ExcludedFolders = saved.ExcludedFolders });
        Assert.Equal(["vendor"], (await Store.LoadAsync(Token)).ExcludedFolders);

        var raw = _app.Services.GetRequiredService<IUserSettingsStore>();
        await raw.SetAsync(UserSettingKeys.AnalysisMaxFiles, "-5", Token);
        await raw.SetAsync(UserSettingKeys.AnalysisExcludedFolders, "vendor\n../x\nok", Token);
        var loaded = await Store.LoadAsync(Token);

        Assert.Equal(AnalysisSettings.Default.MaxFiles, loaded.MaxFiles);
        Assert.Equal(10, loaded.HistoryMaxCommits);
        Assert.Equal(["vendor", "ok"], loaded.ExcludedFolders);
        Assert.Equal(SettingsErrorCodes.Invalid, (await Store.SaveAsync(AnalysisSettings.Default with { ParseTimeoutSeconds = 0 }, Token)).Error.Code);
    }

    [Fact]
    public async Task Import_UsesTheExcludedFoldersAndFileSizeLimit()
    {
        using var copy = ZeroExecutionHarness.CopyFixture("dotnet-layered");
        await Store.SaveAsync(AnalysisSettings.Default with { ExcludedFolders = ["tests"] }, Token);

        var summary = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(copy.FullPath, null, Token)).Value;
        var stored = await _app.Services.GetRequiredService<IArchitectureModelStore>().LoadCurrentModelAsync(
            new RepositoryId(Guid.ParseExact(summary.RepositoryId, "N")), Token);

        Assert.DoesNotContain(stored!.Model.Files, f => f.Path.Value.StartsWith("tests/", StringComparison.Ordinal));
        Assert.DoesNotContain(stored.Model.Projects, p => p.Name == "Shop.Tests");
    }

    [Fact]
    public async Task EditorLaunch_IsOnlyBuiltForFilesOfTheModel()
    {
        using var copy = ZeroExecutionHarness.CopyFixture("dotnet-layered");
        var summary = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(copy.FullPath, null, Token)).Value;
        var editor = _app.Services.GetRequiredService<PrepareEditorLaunch>();
        const string File = "src/Shop.Domain/Orders/Order.cs";

        Assert.Equal(EditorErrorCodes.NotConfigured, (await editor.ExecuteAsync(summary.RepositoryId, File, 3, Token)).Error.Code);

        Assert.True((await editor.SaveTemplateAsync("ed --line {line} {path}", Token)).IsSuccess);
        var launch = (await editor.ExecuteAsync(summary.RepositoryId, File, 3, Token)).Value;
        Assert.Equal("ed", launch.FileName);
        Assert.Equal(["--line", "3", Path.Combine(copy.FullPath, "src", "Shop.Domain", "Orders", "Order.cs")], launch.Arguments);

        Assert.Equal(CodebaseExplorer.Application.Source.SourceErrorCodes.FileNotInModel,
            (await editor.ExecuteAsync(summary.RepositoryId, "../../outside.cs", 1, Token)).Error.Code);
    }

    [Fact]
    public async Task Import_UsesTheGitHistoryWindow()
    {
        using var built = GitRepositoryBuilder.Create()
            .Write("a.txt", "1\n").Commit("one")
            .Write("a.txt", "2\n").Commit("two")
            .Write("a.txt", "3\n").Commit("three")
            .Build();
        await Store.SaveAsync(AnalysisSettings.Default with { HistoryMaxCommits = 2, HistoryDays = 0 }, Token);

        var summary = (await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(built.WorkingDirectory, null, Token)).Value;
        var activity = await _app.Services.GetRequiredService<IGitActivityStore>().LoadCurrentAsync(
            new RepositoryId(Guid.ParseExact(summary.RepositoryId, "N")), Token);

        Assert.Equal(2, activity!.Snapshot.HistoryDepth);
    }
}
