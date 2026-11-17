using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Tests.Common;

public sealed class SourceLocationTests
{
    private static readonly ElementId File = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("file"));

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(3, 10, 3, 12)]
    [InlineData(3, 10, 5, 1)]
    public void Constructor_AcceptsOrderedRanges(int startLine, int startColumn, int endLine, int endColumn)
    {
        var location = new SourceLocation(File, startLine, startColumn, endLine, endColumn);

        Assert.Equal(startLine, location.StartLine);
        Assert.Equal(endColumn, location.EndColumn);
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 0)]
    public void Constructor_RejectsNonPositivePositions(int startLine, int startColumn, int endLine, int endColumn) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceLocation(File, startLine, startColumn, endLine, endColumn));

    [Theory]
    [InlineData(5, 1, 4, 9)]
    [InlineData(5, 7, 5, 6)]
    public void Constructor_RejectsRangesEndingBeforeStart(int startLine, int startColumn, int endLine, int endColumn) =>
        Assert.Throws<ArgumentException>(() => new SourceLocation(File, startLine, startColumn, endLine, endColumn));

    [Fact]
    public void Constructor_RejectsMissingFile() =>
        Assert.Throws<ArgumentException>(() => new SourceLocation(default, 1, 1, 1, 1));

    [Fact]
    public void Line_CoversSingleLine()
    {
        var location = SourceLocation.Line(File, 12, 5, 68);

        Assert.Equal((12, 5, 12, 68), (location.StartLine, location.StartColumn, location.EndLine, location.EndColumn));
    }
}
