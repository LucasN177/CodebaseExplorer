using CodebaseExplorer.Domain.Repositories;
using CodebaseExplorer.Storage.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CodebaseExplorer.Storage.Tests;

/// <summary>Writes <see cref="DomainSamples"/> into a <see cref="TestDatabase"/> in foreign-key order.</summary>
internal static class SampleStore
{
    /// <summary>
    /// Order (DATABASE_SCHEMA.md §2): repository, run, files without project, projects, file→project links
    /// and modules, entities with declarations, external dependencies, relationships with evidence, then the
    /// current run of the repository.
    /// </summary>
    public static async Task SaveAsync(TestDatabase database, DomainSamples samples, CancellationToken cancellationToken)
    {
        var runId = samples.Run.Id;
        var model = samples.Model;
        var fileRecords = samples.Files.Select(f => RecordMapper.ToRecord(f, runId)).ToList();
        var projectIds = fileRecords.ToDictionary(f => f.Id, f => f.ProjectId);

        await using var context = database.CreateContext();
        context.Repositories.Add(RecordMapper.ToRecord(Unanalyzed(samples.Repository)));
        await context.SaveChangesAsync(cancellationToken);

        context.AnalysisRuns.Add(RecordMapper.ToRecord(samples.Run));
        fileRecords.ForEach(f => f.ProjectId = null);
        context.Files.AddRange(fileRecords);
        await context.SaveChangesAsync(cancellationToken);

        context.Projects.AddRange(RecordMapper.ToRecord(samples.Web, runId), RecordMapper.ToRecord(samples.Legacy, runId));
        await context.SaveChangesAsync(cancellationToken);

        fileRecords.ForEach(f => f.ProjectId = projectIds[f.Id]);
        context.Modules.AddRange(RecordMapper.ToRecord(samples.RootModule, runId), RecordMapper.ToRecord(samples.ChildModule, runId));
        context.CodeEntities.AddRange(samples.Entities.Select(e => RecordMapper.ToRecord(e, runId)));
        context.ExternalDependencies.Add(RecordMapper.ToRecord(samples.Serilog, runId));
        context.CodeRelationships.AddRange(samples.Relationships.Select(r => RecordMapper.ToRecord(r, model, runId)));
        context.AnalysisEvidence.AddRange(samples.Relationships.SelectMany(r => RecordMapper.ToEvidenceRecords(r, samples.Repository.Id)));
        await context.SaveChangesAsync(cancellationToken);

        (await context.Repositories.SingleAsync(cancellationToken)).LastAnalysisRunId = runId.Value;
        await context.SaveChangesAsync(cancellationToken);
    }

    public static Repository Unanalyzed(Repository repository) =>
        new(repository.Id, repository.Name, repository.Location, repository.AddedAt);
}
