using CodebaseExplorer.Application.Export;
using CodebaseExplorer.Application.Graph;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Application.Settings;
using CodebaseExplorer.Contracts.Export;
using CodebaseExplorer.Contracts.Graph;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels.Pages;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>
/// EXPORT-002: the export page — real use cases, store and file writer; substituted folder picker, folder launcher and
/// graph host. Writing into the analyzed repository needs an explicit confirmation (SECURITY.md S5).
/// </summary>
public sealed class ExportPageViewModelTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();
    private readonly TempDirectory _repository = new();
    private readonly TempDirectory _target = new();
    private readonly IFolderPicker _picker = Substitute.For<IFolderPicker>();
    private readonly IFolderLauncher _launcher = Substitute.For<IFolderLauncher>();
    private readonly IGraphViewHost _host = Substitute.For<IGraphViewHost>();
    private readonly RepositorySession _session = new();
    private readonly ShellStatus _shell = new(TimeProvider.System);
    private ArchitectureExplorerPageViewModel _explorer = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _app.InitializeAsync();
        _host.ShowGraphAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(true);
        _host.Selection.Returns(GraphSelection.Empty);
        _launcher.OpenAsync(Arg.Any<string>()).Returns(true);
        _explorer = new ArchitectureExplorerPageViewModel(
            _host, Get<GetGraphView>(), Get<GetGraphEdgeDetails>(), Get<CodebaseExplorer.Application.Source.GetSourceSnippet>(),
            Get<CodebaseExplorer.Application.Inspector.GetElementDetails>(), Get<CodebaseExplorer.Application.Search.SearchElements>(),
            Substitute.For<IClipboardService>(), _session, Substitute.For<IUserSettingsStore>(), _shell,
            NullLogger<ArchitectureExplorerPageViewModel>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _repository.Dispose();
        _target.Dispose();
    }

    [Fact]
    public void WithoutRepository_ShowsTheEmptyStateAndCannotExport()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.ShowEmptyState);
        Assert.False(viewModel.ExportCommand.CanExecute(null));
        Assert.Equal(ReportSections.All, viewModel.Sections.Select(s => s.Token));
        Assert.All(viewModel.Sections, s => Assert.True(s.IsSelected));
        Assert.False(string.IsNullOrWhiteSpace(viewModel.TargetFolder));
    }

    [Fact]
    public async Task Export_WritesTheReportIntoTheChosenFolder_AndOpensIt()
    {
        await ImportAsync();
        var viewModel = CreateViewModel();
        _picker.PickFolderAsync(default!, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(_target.FullPath);
        await viewModel.ChooseFolderCommand.ExecuteAsync(null);
        viewModel.Sections.Single(s => s.Token == ReportSections.EvidenceStatistics).IsSelected = false;

        Assert.False(viewModel.TargetIsInsideRepository);
        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        var file = Assert.Single(Directory.GetFiles(_target.FullPath));
        Assert.Matches(@"^ARCHITECTURE-[0-9a-f]{32}-\d{4}-\d{2}-\d{2}\.md$", Path.GetFileName(file));
        var text = await File.ReadAllTextAsync(file, Ct);
        Assert.StartsWith("# Architecture report:", text, StringComparison.Ordinal);
        Assert.Contains("Scope: whole repository.", text, StringComparison.Ordinal);
        Assert.Contains("## Dependency graph", text, StringComparison.Ordinal);
        Assert.DoesNotContain("## Evidence statistics", text, StringComparison.Ordinal);
        Assert.StartsWith("Saved ARCHITECTURE-", viewModel.StatusMessage, StringComparison.Ordinal);
        await _launcher.Received(1).OpenAsync(_target.FullPath);

        // A second export never overwrites the first one.
        await viewModel.ExportCommand.ExecuteAsync(null);
        Assert.Equal(2, Directory.GetFiles(_target.FullPath).Length);
    }

    [Fact]
    public async Task TargetInsideTheRepository_NeedsExplicitConfirmation()
    {
        await ImportAsync();
        var viewModel = CreateViewModel();
        var inside = Path.Combine(_repository.FullPath, "docs");

        viewModel.TargetFolder = inside;

        Assert.True(viewModel.TargetIsInsideRepository);
        Assert.False(viewModel.ExportCommand.CanExecute(null));

        viewModel.ConfirmWriteIntoRepository = true;
        Assert.True(viewModel.ExportCommand.CanExecute(null));
        viewModel.OpenFolderAfterExport = false;
        await viewModel.ExportCommand.ExecuteAsync(null);
        Assert.Single(Directory.GetFiles(inside));
        await _launcher.DidNotReceiveWithAnyArgs().OpenAsync(default!);

        // Another target inside the repository asks again.
        viewModel.TargetFolder = _repository.FullPath;
        Assert.False(viewModel.ConfirmWriteIntoRepository);
        Assert.False(viewModel.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task NoSectionOrRelativeTarget_CannotExport()
    {
        await ImportAsync();
        var viewModel = CreateViewModel();
        viewModel.TargetFolder = _target.FullPath;
        Assert.True(viewModel.ExportCommand.CanExecute(null));

        foreach (var section in viewModel.Sections)
        {
            section.IsSelected = false;
        }

        Assert.False(viewModel.ExportCommand.CanExecute(null));
        viewModel.Sections[0].IsSelected = true;
        viewModel.TargetFolder = "relative/folder";
        Assert.False(viewModel.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task CurrentView_ExportsTheExplorersGraph()
    {
        await ImportAsync();
        await WaitUntilAsync(() => _explorer.CurrentQuery is not null);
        var viewModel = CreateViewModel();
        viewModel.TargetFolder = _target.FullPath;
        viewModel.OpenFolderAfterExport = false;

        Assert.True(viewModel.CanUseCurrentView);
        viewModel.UseCurrentView = true;
        await viewModel.ExportCommand.ExecuteAsync(null);

        var text = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_target.FullPath)), Ct);
        Assert.Contains("Scope: current graph view.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteFailure_IsShownInline()
    {
        await ImportAsync();
        var viewModel = CreateViewModel();
        viewModel.TargetFolder = _target.WriteText("a-file.txt", "not a folder");

        await viewModel.ExportCommand.ExecuteAsync(null);

        Assert.StartsWith("The report could not be written to", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Null(viewModel.StatusMessage);
    }

    [Theory]
    [InlineData("repo", true)]
    [InlineData("repo/docs/out", true)]
    [InlineData("repo2", false)]
    [InlineData("other", false)]
    public void IsInsideRepository_ComparesWholePathSegments(string folder, bool expected)
    {
        Assert.Equal(expected, ReportFileWriter.IsInsideRepository(_target.PathOf(folder), _target.PathOf("repo") + Path.DirectorySeparatorChar));
    }

    private async Task ImportAsync()
    {
        var source = TestRepositoryCatalog.GetFixturePath("dotnet-layered");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(_repository.FullPath, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        var summary = (await Get<ImportLocalRepository>().ExecuteAsync(_repository.FullPath, null, Ct)).Value;
        _session.SetCurrent(new OpenRepositoryDto
        {
            RepositoryId = summary.RepositoryId,
            Name = summary.Name,
            RootPath = summary.RootPath,
            AnalysisRunId = summary.AnalysisRunId,
        });
    }

    private ExportPageViewModel CreateViewModel() => new(
        Get<ExportArchitectureReport>(), _explorer, _session, _picker, _launcher, new ReportFileWriter(), _shell,
        NullLogger<ExportPageViewModel>.Instance);

    private T Get<T>()
        where T : notnull => _app.Services.GetRequiredService<T>();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var waited = 0; !condition() && waited < 5000; waited += 20)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
