using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.AI;

/// <summary>
/// A fixture copied to <c>&lt;temp&gt;/&lt;fixture name&gt;</c>, so the repository name (the folder name) is stable and
/// prompts can be compared with snapshots.
/// </summary>
internal sealed class NamedFixtureCopy : IDisposable
{
    private readonly TempDirectory _root = new();

    public NamedFixtureCopy(string fixture)
    {
        var source = TestRepositoryCatalog.GetFixturePath(fixture);
        FullPath = Path.Combine(_root.FullPath, fixture);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(FullPath, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    public string FullPath { get; }

    public void WriteText(string relativePath, string content)
    {
        var path = Path.Combine(FullPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Imports the copy and returns the stored model.</summary>
    public async Task<StoredRepositoryModel> ImportAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var summary = (await services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(FullPath, null, cancellationToken)).Value;
        var id = new RepositoryId(Guid.ParseExact(summary.RepositoryId, "N"));
        return (await services.GetRequiredService<IArchitectureModelStore>().LoadCurrentModelAsync(id, cancellationToken))!;
    }

    public void Dispose() => _root.Dispose();
}
