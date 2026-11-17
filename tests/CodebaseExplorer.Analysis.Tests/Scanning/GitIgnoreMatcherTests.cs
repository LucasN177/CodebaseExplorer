using CodebaseExplorer.Analysis.Scanning;

namespace CodebaseExplorer.Analysis.Tests.Scanning;

public sealed class GitIgnoreMatcherTests
{
    [Fact]
    public void LastMatchingRuleWins_NegationReIncludes()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddFile(string.Empty, "*.log\n!keep.log\n");

        Assert.True(matcher.IsIgnored("a.log", isDirectory: false));
        Assert.False(matcher.IsIgnored("keep.log", isDirectory: false));
        Assert.False(matcher.IsIgnored("dir/keep.log", isDirectory: false));
    }

    [Fact]
    public void NestedFile_OverridesParentRules()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddFile(string.Empty, "*.txt\n");
        matcher.AddFile("sub", "!b.txt\n");

        Assert.True(matcher.IsIgnored("a.txt", isDirectory: false));
        Assert.True(matcher.IsIgnored("sub/a.txt", isDirectory: false));
        Assert.False(matcher.IsIgnored("sub/b.txt", isDirectory: false));
        Assert.True(matcher.IsIgnored("b.txt", isDirectory: false));
    }

    [Fact]
    public void LaterPositiveRule_IgnoresAgain()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddFile(string.Empty, "*.log\n!important.log\nimportant.log\n");

        Assert.True(matcher.IsIgnored("important.log", isDirectory: false));
    }

    [Fact]
    public void CommentsAndBlankLines_AddNoRules()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddFile(string.Empty, "# comment\n\n   \n*.o\n");

        Assert.Equal(1, matcher.RuleCount);
    }

    [Fact]
    public void PathologicalPattern_DoesNotHang()
    {
        var matcher = new GitIgnoreMatcher();
        matcher.AddFile(string.Empty, string.Concat(Enumerable.Repeat("*a", 40)) + "\n");

        var path = new string('a', 60) + "b";

        Assert.False(matcher.IsIgnored(path, isDirectory: false));
    }
}
