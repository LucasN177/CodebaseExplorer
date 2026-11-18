using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Projects;
using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Storage.Persistence;
using CodebaseExplorer.Storage.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Tests.Persistence;

/// <summary>
/// STORE-001: core tables round-trip Domain → record → SQLite → record → Domain, with the conventions of
/// DATABASE_SCHEMA.md §1 (BLOB Guids, TEXT enums, pragmas, cascades).
/// </summary>
public sealed class CoreTablesTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly DomainSamples _samples = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Repository_RoundTrips_LocalAndCloned()
    {
        var cloned = new Repository(RepositoryId.New(), "remote", RepositoryLocation.Cloned(
            Path.Combine(Path.GetTempPath(), "clones", "remote"), new Uri("https://github.com/org/remote.git"), RepositorySourceKind.GitHub),
            DateTimeOffset.UtcNow);
        var lastOpened = new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

        await using (var context = _database.CreateContext())
        {
            context.Repositories.Add(RecordMapper.ToRecord(Unanalyzed(_samples.Repository), lastOpened));
            context.Repositories.Add(RecordMapper.ToRecord(cloned));
            await context.SaveChangesAsync(Token);
        }

        await using var read = _database.CreateContext();
        var local = await read.Repositories.SingleAsync(r => r.Id == _samples.Repository.Id.Value, Token);
        var remote = RecordMapper.ToDomain(await read.Repositories.SingleAsync(r => r.Id == cloned.Id.Value, Token));

        AssertSameRepository(Unanalyzed(_samples.Repository), RecordMapper.ToDomain(local));
        Assert.Equal(lastOpened, local.LastOpenedAt);
        AssertSameRepository(cloned, remote);
    }

    [Fact]
    public async Task AnalysisRun_RoundTrips_WithDiagnosticsStatisticsAndAnalyzers()
    {
        await SaveAllAsync();

        await using var context = _database.CreateContext();
        var record = await context.AnalysisRuns.Include(r => r.Diagnostics).SingleAsync(Token);
        var run = RecordMapper.ToDomain(record);

        var expected = _samples.Run;
        Assert.Equal((expected.Id, expected.RepositoryId, expected.Status, expected.Trigger), (run.Id, run.RepositoryId, run.Status, run.Trigger));
        Assert.Equal(expected.StartedAt, run.StartedAt);
        Assert.Equal(expected.CompletedAt, run.CompletedAt);
        Assert.Equal((expected.CommitSha, expected.Branch), (run.CommitSha, run.Branch));
        Assert.Equal(expected.AnalyzerVersions, run.AnalyzerVersions);
        Assert.Equal(expected.Diagnostics, run.Diagnostics); // records: value equality, order preserved
        Assert.Equal(expected.Statistics.FilesScanned, run.Statistics.FilesScanned);
        Assert.Equal(expected.Statistics.RelationshipsByLevel, run.Statistics.RelationshipsByLevel);
        Assert.Equal(expected.Statistics.StageDurations, run.Statistics.StageDurations);
    }

    [Fact]
    public async Task FilesProjectsModules_RoundTrip()
    {
        await SaveAllAsync();

        await using var context = _database.CreateContext();
        var files = (await context.Files.ToListAsync(Token)).Select(RecordMapper.ToDomain).ToList();
        var paths = files.ToDictionary(f => f.Id.Value, f => f.Path.Value);
        var projects = (await context.Projects.ToListAsync(Token)).Select(p => RecordMapper.ToDomain(p, paths)).ToList();
        var modules = RecordMapper.ToDomain(await context.Modules.ToListAsync(Token), projects.ToDictionary(p => p.Id));

        foreach (var expected in _samples.Files)
        {
            var actual = files.Single(f => f.Id == expected.Id);
            Assert.Equal((expected.Path, expected.Language, expected.Classification, expected.SizeBytes, expected.LineCount, expected.ProjectId),
                (actual.Path, actual.Language, actual.Classification, actual.SizeBytes, actual.LineCount, actual.ProjectId));
            Assert.Equal(expected.Hash, actual.Hash);
        }

        var web = projects.Single(p => p.Id == _samples.Web.Id);
        Assert.Equal(_samples.Web.ProjectFilePath, web.ProjectFilePath);
        Assert.Equal(_samples.Web.ProjectFileId, web.ProjectFileId);
        Assert.Equal(_samples.Web.Properties, web.Properties);
        Assert.Equal(_samples.Web.SolutionMemberships, web.SolutionMemberships);
        Assert.Equal((ProjectKind.DotNetProject, Ecosystem.DotNet, LanguageId.CSharp), (web.Kind, web.Ecosystem, web.PrimaryLanguage));

        var legacy = projects.Single(p => p.Id == _samples.Legacy.Id);
        Assert.Null(legacy.ProjectFilePath);
        Assert.True(legacy.IsPlaceholder());

        var child = modules.Single(m => m.Id == _samples.ChildModule.Id);
        Assert.Equal((_samples.RootModule.Id, "Web.Controllers", _samples.Web.Id), (child.ParentModuleId!.Value, child.QualifiedName, child.ProjectId));
        Assert.Equal(2, modules.Count);
    }

    [Fact]
    public async Task GuidColumns_AreSixteenByteBlobs_AndEnumsAreText()
    {
        await SaveAllAsync();

        Assert.Equal("blob", await _database.ScalarAsync("SELECT typeof(Id) FROM Repositories"));
        Assert.Equal(16L, await _database.ScalarAsync("SELECT length(Id) FROM Repositories"));
        Assert.Equal("blob", await _database.ScalarAsync("SELECT typeof(LastAnalysisRunId) FROM Repositories"));
        Assert.Equal("blob", await _database.ScalarAsync("SELECT typeof(ProjectId) FROM Files WHERE ProjectId IS NOT NULL LIMIT 1"));
        Assert.Equal("blob", await _database.ScalarAsync("SELECT typeof(ParentModuleId) FROM Modules WHERE ParentModuleId IS NOT NULL"));
        Assert.Equal(32L, await _database.ScalarAsync("SELECT length(ContentHash) FROM Files LIMIT 1"));
        Assert.Equal("Local", await _database.ScalarAsync("SELECT SourceKind FROM Repositories"));
        Assert.Equal("Succeeded", await _database.ScalarAsync("SELECT Status FROM AnalysisRuns"));
        Assert.Equal("DotNetProject", await _database.ScalarAsync("SELECT Kind FROM Projects WHERE Name = 'Web'"));
    }

    [Fact]
    public async Task Timestamps_AreFixedWidthIsoUtcText()
    {
        await SaveAllAsync();

        // Started at 10:30 +01:00 → stored as the same instant in UTC.
        Assert.Equal("2026-11-18T09:30:00.0000000Z", await _database.ScalarAsync("SELECT StartedAt FROM AnalysisRuns"));
        Assert.Equal("2026-11-18T09:30:03.0000000Z", await _database.ScalarAsync("SELECT CompletedAt FROM AnalysisRuns"));
        Assert.Equal("text", await _database.ScalarAsync("SELECT typeof(AddedAt) FROM Repositories"));
    }

    [Fact]
    public async Task Connection_UsesWalForeignKeysAndBusyTimeout()
    {
        Assert.Equal("wal", await _database.ScalarAsync("PRAGMA journal_mode"));
        Assert.Equal(1L, await _database.ScalarAsync("PRAGMA foreign_keys"));
        Assert.Equal(5000L, await _database.ScalarAsync("PRAGMA busy_timeout"));
    }

    [Fact]
    public async Task Schema_HasTheTablesAndIndexesOfTheSpecification()
    {
        await using var context = _database.CreateContext();
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT type, name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'";
        var objects = new HashSet<string>();
        await using (var reader = await command.ExecuteReaderAsync(Token))
        {
            while (await reader.ReadAsync(Token))
            {
                objects.Add($"{reader.GetString(0)}:{reader.GetString(1)}");
            }
        }

        string[] expected =
        [
            "table:Repositories", "table:AnalysisRuns", "table:AnalysisDiagnostics", "table:Files", "table:Projects", "table:Modules",
            "index:UX_Repositories_LocalPath", "index:IX_Repositories_LastOpenedAt",
            "index:IX_AnalysisRuns_RepositoryId_StartedAt", "index:IX_AnalysisDiagnostics_AnalysisRunId_Severity",
            "index:UX_Files_RepositoryId_RelativePath", "index:IX_Files_ProjectId", "index:IX_Files_RepositoryId_Language",
            "index:IX_Projects_RepositoryId_Ecosystem", "index:IX_Projects_RepositoryId_Name",
            "index:IX_Modules_ProjectId", "index:IX_Modules_ParentModuleId", "index:IX_Modules_RepositoryId_QualifiedName",
        ];
        Assert.All(expected, name => Assert.Contains(name, objects));
    }

    [Fact]
    public async Task DeletingTheRepository_CascadesToAllModelData()
    {
        await SaveAllAsync();

        await using (var context = _database.CreateContext())
        {
            context.Repositories.Remove(await context.Repositories.SingleAsync(Token));
            await context.SaveChangesAsync(Token);
        }

        foreach (var table in new[] { "Repositories", "AnalysisRuns", "AnalysisDiagnostics", "Files", "Projects", "Modules",
            "CodeEntities", "CodeEntityDeclarations", "ExternalDependencies", "CodeRelationships", "AnalysisEvidence" })
        {
            Assert.Equal(0L, await _database.ScalarAsync($"SELECT count(*) FROM {table}"));
        }
    }

    [Fact]
    public async Task ForeignKeys_AreEnforced()
    {
        await using var context = _database.CreateContext();
        context.Files.Add(RecordMapper.ToRecord(_samples.Readme, _samples.Run.Id)); // repository row does not exist

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task Runs_CanBeOrderedByStartTime()
    {
        // Timestamps are TEXT (ISO-8601, written in UTC); ordering must translate to SQL (DATABASE_SCHEMA.md §1).
        await SaveAllAsync();
        var later = Domain.Runs.AnalysisRun.Create(_samples.Repository.Id, Domain.Runs.AnalysisTrigger.Manual, _samples.Run.StartedAt.AddHours(1));
        var earlier = Domain.Runs.AnalysisRun.Create(_samples.Repository.Id, Domain.Runs.AnalysisTrigger.Manual, _samples.Run.StartedAt.AddHours(-1));
        await using (var context = _database.CreateContext())
        {
            context.AnalysisRuns.AddRange(RecordMapper.ToRecord(later), RecordMapper.ToRecord(earlier));
            await context.SaveChangesAsync(Token);
        }

        await using var read = _database.CreateContext();
        var ordered = await read.AnalysisRuns
            .Where(r => r.RepositoryId == _samples.Repository.Id.Value)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => r.Id)
            .ToListAsync(Token);

        Assert.Equal([later.Id.Value, _samples.Run.Id.Value, earlier.Id.Value], ordered);
    }

    [Fact]
    public async Task MismatchingNaturalKey_IsDetectedOnRead()
    {
        await SaveAllAsync();
        await using (var context = _database.CreateContext())
        {
            await context.Files
                .Where(f => f.Id == _samples.Readme.Id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.RelativePath, "OTHER.md"), Token);
        }

        await using var read = _database.CreateContext();
        var record = await read.Files.SingleAsync(f => f.Id == _samples.Readme.Id.Value, Token);

        Assert.Throws<InvalidDataException>(() => RecordMapper.ToDomain(record));
    }

    [Fact]
    public async Task ProjectWithUnknownProjectFile_IsRejectedOnRead()
    {
        await SaveAllAsync();
        await using var context = _database.CreateContext();
        var record = await context.Projects.SingleAsync(p => p.Id == _samples.Web.Id.Value, Token);

        Assert.Throws<InvalidDataException>(() => RecordMapper.ToDomain(record, new Dictionary<Guid, string>()));
    }

    [Fact]
    public void PersistenceEntitiesAndContext_AreInternal()
    {
        var types = typeof(CodebaseExplorerDbContext).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("CodebaseExplorer.Storage.Persistence", StringComparison.Ordinal) == true);

        Assert.All(types, t => Assert.False(t.IsVisible, $"{t.FullName} must not be public"));
        Assert.Contains(types, t => t == typeof(RepositoryRecord));
    }

    private Task SaveAllAsync() => SampleStore.SaveAsync(_database, _samples, Token);

    private static Repository Unanalyzed(Repository repository) => SampleStore.Unanalyzed(repository);

    private static void AssertSameRepository(Repository expected, Repository actual)
    {
        Assert.Equal((expected.Id, expected.Name, expected.Location, expected.LastAnalysisRunId),
            (actual.Id, actual.Name, actual.Location, actual.LastAnalysisRunId));
        Assert.Equal(expected.AddedAt, actual.AddedAt); // same instant; stored in UTC
    }
}
