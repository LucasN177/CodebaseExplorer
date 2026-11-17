using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Repositories;

namespace CodebaseExplorer.Domain.Tests.Repositories;

public sealed class RepositoryTests
{
    private static readonly RepositoryLocation Location =
        RepositoryLocation.Local(Path.Combine(Path.GetTempPath(), "ce-repo"));

    [Fact]
    public void Register_CreatesTimeOrderedIdAndTrimsName()
    {
        var addedAt = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

        var repository = Repository.Register("  shop  ", Location, addedAt);

        Assert.Equal(7, repository.Id.Value.Version);
        Assert.Equal("shop", repository.Name);
        Assert.Equal(addedAt, repository.AddedAt);
        Assert.Null(repository.LastAnalysisRunId);
    }

    [Fact]
    public void Constructor_RejectsMissingIdOrName()
    {
        Assert.Throws<ArgumentException>(() => new Repository(default, "shop", Location, DateTimeOffset.UnixEpoch));
        Assert.ThrowsAny<ArgumentException>(() => new Repository(RepositoryId.New(), " ", Location, DateTimeOffset.UnixEpoch));
    }
}
