using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;
using CodebaseExplorer.Domain.Modules;
using CodebaseExplorer.Domain.Projects;

using CodebaseExplorer.Domain.Tests.Projects;

namespace CodebaseExplorer.Domain.Tests.Files;

public sealed class RepositoryFileTests
{
    [Fact]
    public void Create_DerivesIdFromNaturalKey()
    {
        var file = RepositoryFile.Create(
            Sample.Repository, RelativePath.Create("src/A.cs"), LanguageId.CSharp, FileClassification.Source, 120, Sample.Hash, lineCount: 10);

        Assert.Equal(NaturalKey.ForFile(Sample.Repository, "src/A.cs"), file.NaturalKey);
        Assert.Equal(ElementId.FromNaturalKey(file.NaturalKey), file.Id);
        Assert.Equal(RepositoryFile.IdFor(Sample.Repository, RelativePath.Create("src/A.cs")), file.Id);
    }

    [Fact]
    public void Create_SamePathTwice_YieldsSameId()
    {
        var first = RepositoryFile.Create(Sample.Repository, RelativePath.Create("a.py"), LanguageId.Python, FileClassification.Source, 1, Sample.Hash);
        var second = RepositoryFile.Create(Sample.Repository, RelativePath.Create(@".\a.py"), LanguageId.Python, FileClassification.Test, 2, Sample.Hash);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Create_RejectsRootPath() =>
        Assert.Throws<ArgumentException>(() =>
            RepositoryFile.Create(Sample.Repository, RelativePath.Root, LanguageId.Unknown, FileClassification.Other, 0, Sample.Hash));

    [Fact]
    public void Create_RejectsNegativeNumbers()
    {
        var path = RelativePath.Create("a.cs");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RepositoryFile.Create(Sample.Repository, path, LanguageId.CSharp, FileClassification.Source, -1, Sample.Hash));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RepositoryFile.Create(Sample.Repository, path, LanguageId.CSharp, FileClassification.Source, 0, Sample.Hash, lineCount: -1));
    }

    [Fact]
    public void Create_RejectsMissingRepositoryOrHash()
    {
        var path = RelativePath.Create("a.cs");

        Assert.Throws<ArgumentException>(() =>
            RepositoryFile.Create(default, path, LanguageId.CSharp, FileClassification.Source, 0, Sample.Hash));
        Assert.Throws<ArgumentNullException>(() =>
            RepositoryFile.Create(Sample.Repository, path, LanguageId.CSharp, FileClassification.Source, 0, null!));
    }

    [Fact]
    public void Create_RejectsUndefinedClassification() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RepositoryFile.Create(Sample.Repository, RelativePath.Create("a.cs"), LanguageId.CSharp, (FileClassification)99, 0, Sample.Hash));
}
