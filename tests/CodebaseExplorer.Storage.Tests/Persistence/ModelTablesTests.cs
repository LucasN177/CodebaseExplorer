using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Entities;
using CodebaseExplorer.Domain.Evidence;
using CodebaseExplorer.Domain.Relationships;
using CodebaseExplorer.Storage.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Tests.Persistence;

/// <summary>
/// STORE-002: entities, declarations, external dependencies, relationships and evidence round-trip
/// through SQLite; the indexes of DATABASE_SCHEMA.md §4.7–§4.11 exist.
/// </summary>
public sealed class ModelTablesTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly DomainSamples _samples = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Entities_RoundTrip_WithDeclarationsContainmentAndMetrics()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);

        var entities = await ReadEntitiesAsync();

        Assert.Equal(_samples.Entities.Select(e => e.Id.Value).Order(), entities.Select(e => e.Id.Value).Order());
        foreach (var expected in _samples.Entities)
        {
            var actual = entities.Single(e => e.Id == expected.Id);
            Assert.Equal((expected.Kind, expected.Name, expected.QualifiedName, expected.Language, expected.SymbolKey),
                (actual.Kind, actual.Name, actual.QualifiedName, actual.Language, actual.SymbolKey));
            Assert.Equal((expected.Visibility, expected.Modifiers, expected.Signature, expected.Metrics),
                (actual.Visibility, actual.Modifiers, actual.Signature, actual.Metrics));
            Assert.Equal((expected.ProjectId, expected.ModuleId, expected.ContainingEntityId), (actual.ProjectId, actual.ModuleId, actual.ContainingEntityId));
            Assert.Equal(expected.Declarations, actual.Declarations); // order of partial declarations preserved
        }

        Assert.Equal(EntityModifiers.Partial | EntityModifiers.Sealed, entities.Single(e => e.Name == "HomeController").Modifiers);
    }

    [Fact]
    public async Task ExternalDependency_RoundTrips()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);

        await using var context = _database.CreateContext();
        var dependency = RecordMapper.ToDomain(await context.ExternalDependencies.SingleAsync(Token));

        Assert.Equal((_samples.Serilog.Id, _samples.Serilog.Ecosystem, _samples.Serilog.Name, _samples.Serilog.DisplayName),
            (dependency.Id, dependency.Ecosystem, dependency.Name, dependency.DisplayName));
    }

    [Fact]
    public async Task Relationships_RoundTrip_WithEvidenceAttributesAndOrigin()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);

        var relationships = await ReadRelationshipsAsync();

        foreach (var expected in _samples.Relationships)
        {
            var actual = relationships.Single(r => r.Id == expected.Id);
            Assert.Equal((expected.Source, expected.Kind, expected.Target, expected.Level, expected.Occurrences, expected.Origin),
                (actual.Source, actual.Kind, actual.Target, actual.Level, actual.Occurrences, actual.Origin));
            Assert.Equal(expected.Attributes, actual.Attributes);
            Assert.Equal(
                expected.Evidence.Select(e => (e.Id, e.Analyzer, e.AnalyzerVersion, e.Level, e.Rule, e.Location, e.Excerpt, e.AnalysisRunId)).OrderBy(e => e.Id.Value),
                actual.Evidence.Select(e => (e.Id, e.Analyzer, e.AnalyzerVersion, e.Level, e.Rule, e.Location, e.Excerpt, e.AnalysisRunId)).OrderBy(e => e.Id.Value));
        }

        var usesType = relationships.Single(r => r.Kind == RelationshipKind.UsesType);
        Assert.Equal(3, usesType.Occurrences);
        Assert.Contains(usesType.Evidence, e => e.Location is null && e.Level == EvidenceLevel.Heuristic);
    }

    [Fact]
    public async Task Relationships_StoreLevelRankOccurrencesAndContainerIds()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);

        await using var context = _database.CreateContext();
        var rows = await context.CodeRelationships.ToDictionaryAsync(r => r.Kind, Token);

        var usesType = rows[RelationshipKind.UsesType];
        Assert.Equal((EvidenceLevel.Confirmed, 3, 3), (usesType.Level, usesType.LevelRank, usesType.Occurrences));
        Assert.Equal((_samples.Web.Id.Value, _samples.Web.Id.Value), (usesType.SourceProjectId, usesType.TargetProjectId));
        Assert.Equal((_samples.ChildModule.Id.Value, _samples.RootModule.Id.Value), (usesType.SourceModuleId, usesType.TargetModuleId));

        var reference = rows[RelationshipKind.ProjectReference];
        Assert.Equal((EvidenceLevel.Syntactic, 2), (reference.Level, reference.LevelRank));
        Assert.Equal((_samples.Web.Id.Value, _samples.Legacy.Id.Value), (reference.SourceProjectId, reference.TargetProjectId));
        Assert.Null(reference.SourceModuleId);

        var package = rows[RelationshipKind.PackageReference];
        Assert.Null(package.TargetProjectId); // external dependencies belong to no project
        Assert.Equal("Project", await _database.ScalarAsync($"SELECT SourceKind FROM CodeRelationships WHERE Kind = 'PackageReference'"));
        Assert.Equal("ExternalDependency", await _database.ScalarAsync("SELECT TargetKind FROM CodeRelationships WHERE Kind = 'PackageReference'"));
        Assert.Equal(16L, await _database.ScalarAsync("SELECT length(SourceId) FROM CodeRelationships LIMIT 1"));
        Assert.Equal(20L, await _database.ScalarAsync("SELECT Modifiers FROM CodeEntities WHERE Name = 'HomeController'")); // Partial (16) | Sealed (4) as INTEGER
    }

    [Fact]
    public async Task Schema_HasTheIndexesOfTheSpecification()
    {
        string[] expected =
        [
            "IX_CodeEntities_ModuleId", "IX_CodeEntities_ContainingEntityId", "IX_CodeEntities_RepositoryId_Name", "IX_CodeEntities_ProjectId_Kind",
            "IX_CodeEntityDeclarations_FileId",
            "IX_CodeRelationships_SourceId", "IX_CodeRelationships_TargetId", "IX_CodeRelationships_RepositoryId_Kind_LevelRank",
            "IX_CodeRelationships_SourceProjectId_TargetProjectId", "IX_CodeRelationships_SourceModuleId_TargetModuleId",
            "IX_AnalysisEvidence_RelationshipId", "IX_AnalysisEvidence_FileId",
            "UX_ExternalDependencies_RepositoryId_Ecosystem_Name",
        ];

        foreach (var index in expected)
        {
            Assert.Equal(1L, await _database.ScalarAsync($"SELECT count(*) FROM sqlite_master WHERE type = 'index' AND name = '{index}'"));
        }

        // Uniqueness and polymorphic endpoints: the external-dependency index is unique, endpoints have no foreign keys.
        Assert.Equal(1L, await _database.ScalarAsync("SELECT \"unique\" FROM pragma_index_list('ExternalDependencies') WHERE name = 'UX_ExternalDependencies_RepositoryId_Ecosystem_Name'"));
        Assert.Equal(1L, await _database.ScalarAsync("SELECT count(*) FROM pragma_foreign_key_list('CodeRelationships')")); // only RepositoryId
    }

    [Fact]
    public async Task DeletingARelationship_CascadesToItsEvidence()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);

        await using (var context = _database.CreateContext())
        {
            await context.CodeRelationships.Where(r => r.Id == _samples.UsesType.Id.Value).ExecuteDeleteAsync(Token);
        }

        Assert.Equal(2L, await _database.ScalarAsync("SELECT count(*) FROM AnalysisEvidence")); // the other two relationships' evidence
    }

    [Fact]
    public async Task EvidenceWithChangedNaturalKey_IsDetectedOnRead()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);
        await using (var context = _database.CreateContext())
        {
            await context.AnalysisEvidence
                .Where(e => e.RelationshipId == _samples.ProjectReference.Id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Rule, "tampered"), Token);
        }

        await Assert.ThrowsAsync<InvalidDataException>(ReadRelationshipsAsync);
    }

    [Fact]
    public async Task RelationshipWithChangedOccurrences_IsDetectedOnRead()
    {
        await SampleStore.SaveAsync(_database, _samples, Token);
        await using (var context = _database.CreateContext())
        {
            await context.CodeRelationships
                .Where(r => r.Id == _samples.UsesType.Id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Occurrences, 7), Token);
        }

        await Assert.ThrowsAsync<InvalidDataException>(ReadRelationshipsAsync);
    }

    [Fact]
    public async Task StoredModel_IsAValidArchitectureModelAgain()
    {
        // Rebuild every element from the database and let the Domain validate the result.
        await SampleStore.SaveAsync(_database, _samples, Token);

        await using var context = _database.CreateContext();
        var files = (await context.Files.ToListAsync(Token)).Select(RecordMapper.ToDomain).ToList();
        var paths = files.ToDictionary(f => f.Id.Value, f => f.Path.Value);
        var projects = (await context.Projects.ToListAsync(Token)).Select(p => RecordMapper.ToDomain(p, paths)).ToList();
        var modules = RecordMapper.ToDomain(await context.Modules.ToListAsync(Token), projects.ToDictionary(p => p.Id));
        var entities = await ReadEntitiesAsync();
        var dependencies = (await context.ExternalDependencies.ToListAsync(Token)).Select(RecordMapper.ToDomain).ToList();
        var relationships = await ReadRelationshipsAsync();

        var model = Domain.Model.ArchitectureModel.Create(_samples.Repository.Id, _samples.Run.Id, files, projects, modules, entities, dependencies, relationships);

        model.EnsureValid();
        Assert.Equal(_samples.Model.Relationships.Count, model.Relationships.Count);
    }

    private async Task<IReadOnlyList<CodeEntity>> ReadEntitiesAsync()
    {
        await using var context = _database.CreateContext();
        var files = (await context.Files.ToListAsync(Token)).ToDictionary(f => f.Id, f => f.RelativePath);
        var projects = (await context.Projects.ToListAsync(Token)).Select(p => RecordMapper.ToDomain(p, files)).ToDictionary(p => p.Id);
        var modules = RecordMapper.ToDomain(await context.Modules.ToListAsync(Token), projects).ToDictionary(m => m.Id);
        var records = await context.CodeEntities.Include(e => e.Declarations).ToListAsync(Token);
        return RecordMapper.ToDomain(records, projects, modules);
    }

    private async Task<IReadOnlyList<CodeRelationship>> ReadRelationshipsAsync()
    {
        await using var context = _database.CreateContext();
        var evidence = (await context.AnalysisEvidence.ToListAsync(Token)).ToLookup(e => e.RelationshipId);
        return [.. (await context.CodeRelationships.ToListAsync(Token)).Select(r => RecordMapper.ToDomain(r, evidence[r.Id]))];
    }
}
