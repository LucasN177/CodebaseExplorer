using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Tests.Common;

public sealed class RelativePathTests
{
    [Theory]
    [InlineData("src/A.cs", "src/A.cs")]
    [InlineData(@"src\Sub\A.cs", "src/Sub/A.cs")]
    [InlineData("./src//A.cs", "src/A.cs")]
    [InlineData("src/", "src")]
    [InlineData("Src/A.CS", "Src/A.CS")]
    public void Create_NormalizesPath(string input, string expected) =>
        Assert.Equal(expected, RelativePath.Create(input).Value);

    [Theory]
    [InlineData(".")]
    [InlineData("./")]
    public void Create_Dot_IsRoot(string input)
    {
        var path = RelativePath.Create(input);

        Assert.True(path.IsRoot);
        Assert.Equal(RelativePath.Root, path);
        Assert.Equal(".", path.ToString());
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData(@"\\server\share\a.cs")]
    [InlineData(@"C:\repo\a.cs")]
    [InlineData("c:a.cs")]
    [InlineData("../outside.cs")]
    [InlineData("src/../../outside.cs")]
    [InlineData(@"src\..\a.cs")]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_RejectsRootedEmptyOrEscapingPaths(string input) =>
        Assert.ThrowsAny<ArgumentException>(() => RelativePath.Create(input));

    [Fact]
    public void Default_IsRoot() => Assert.True(default(RelativePath).IsRoot);

    [Fact]
    public void FileName_ReturnsLastSegment()
    {
        Assert.Equal("A.cs", RelativePath.Create("src/Sub/A.cs").FileName);
        Assert.Equal("A.cs", RelativePath.Create("A.cs").FileName);
        Assert.Equal(string.Empty, RelativePath.Root.FileName);
    }

    [Theory]
    [InlineData("src/A/B.cs", "src/A", true)]
    [InlineData("src/A", "src/A", true)]
    [InlineData("src/AB/C.cs", "src/A", false)]
    [InlineData("src/A.cs", "src/a", false)]
    [InlineData("other/A.cs", "src", false)]
    public void IsWithin_ComparesWholeSegments(string path, string directory, bool expected) =>
        Assert.Equal(expected, RelativePath.Create(path).IsWithin(RelativePath.Create(directory)));

    [Fact]
    public void IsWithin_Root_IsAlwaysTrue() =>
        Assert.True(RelativePath.Create("a/b.cs").IsWithin(RelativePath.Root));

    [Fact]
    public void Equality_IsOrdinalAfterNormalization()
    {
        Assert.Equal(RelativePath.Create(@"a\b.cs"), RelativePath.Create("a/b.cs"));
        Assert.NotEqual(RelativePath.Create("a/B.cs"), RelativePath.Create("a/b.cs"));
    }
}
