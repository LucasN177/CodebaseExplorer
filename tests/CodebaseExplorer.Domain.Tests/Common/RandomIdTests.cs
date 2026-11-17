using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Tests.Common;

public sealed class RandomIdTests
{
    [Fact]
    public void RepositoryId_New_CreatesVersion7Guids()
    {
        var id = RepositoryId.New();

        Assert.Equal(7, id.Value.Version);
        Assert.NotEqual(id, RepositoryId.New());
    }

    [Fact]
    public void RepositoryId_New_IsTimeOrdered()
    {
        var earlier = RepositoryId.New(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var later = RepositoryId.New(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

        Assert.True(string.CompareOrdinal(earlier.ToString(), later.ToString()) < 0);
    }

    [Fact]
    public void AnalysisRunId_New_CreatesVersion7Guids() =>
        Assert.Equal(7, AnalysisRunId.New().Value.Version);

    [Fact]
    public void EmptyGuids_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new RepositoryId(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new AnalysisRunId(Guid.Empty));
    }

    [Fact]
    public void ToString_Is32LowercaseHexCharacters()
    {
        Assert.Matches("^[0-9a-f]{32}$", RepositoryId.New().ToString());
        Assert.Matches("^[0-9a-f]{32}$", AnalysisRunId.New().ToString());
    }
}
