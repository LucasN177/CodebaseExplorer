using CodebaseExplorer.Analysis.Scanning;

namespace CodebaseExplorer.Analysis.Tests.Scanning;

/// <summary>Pattern semantics from gitignore(5), evaluated for a .gitignore in the repository root.</summary>
public sealed class GitIgnoreRuleTests
{
    [Theory]
    // Unanchored name patterns match at any depth.
    [InlineData("*.log", "a.log", false, true)]
    [InlineData("*.log", "dir/sub/a.log", false, true)]
    [InlineData("*.log", "a.log.txt", false, false)]
    [InlineData("foo", "a/b/foo", false, true)]
    [InlineData("foo", "a/b/foo", true, true)]
    // A leading slash anchors to the .gitignore directory.
    [InlineData("/build", "build", true, true)]
    [InlineData("/build", "src/build", true, false)]
    // A slash in the middle anchors as well.
    [InlineData("doc/frotz", "doc/frotz", true, true)]
    [InlineData("doc/frotz", "a/doc/frotz", true, false)]
    // Trailing slash: directories only.
    [InlineData("frotz/", "frotz", true, true)]
    [InlineData("frotz/", "a/frotz", true, true)]
    [InlineData("frotz/", "frotz", false, false)]
    // '*' does not cross '/'.
    [InlineData("foo/*", "foo/test.json", false, true)]
    [InlineData("foo/*", "foo/bar/hello.c", false, false)]
    [InlineData("a?c", "abc", false, true)]
    [InlineData("a?c", "a/c", false, false)]
    // '**' forms.
    [InlineData("**/foo", "foo", false, true)]
    [InlineData("**/foo", "x/y/foo", false, true)]
    [InlineData("**/foo/bar", "foo/bar", false, true)]
    [InlineData("**/foo/bar", "x/foo/bar", false, true)]
    [InlineData("abc/**", "abc/x", false, true)]
    [InlineData("abc/**", "abc/x/y", false, true)]
    [InlineData("abc/**", "abc", true, false)]
    [InlineData("a/**/b", "a/b", false, true)]
    [InlineData("a/**/b", "a/x/b", false, true)]
    [InlineData("a/**/b", "a/x/y/b", false, true)]
    [InlineData("a**b", "axyb", false, true)]
    [InlineData("a**b", "ax/yb", false, false)]
    // Character classes.
    [InlineData("*.[oa]", "x.o", false, true)]
    [InlineData("*.[oa]", "x.a", false, true)]
    [InlineData("*.[oa]", "x.c", false, false)]
    [InlineData("[!a]bc", "xbc", false, true)]
    [InlineData("[!a]bc", "abc", false, false)]
    [InlineData("[a-c]x", "bx", false, true)]
    // Escapes and trailing spaces.
    [InlineData(@"\#file", "#file", false, true)]
    [InlineData(@"\!important", "!important", false, true)]
    [InlineData("foo   ", "foo", false, true)]
    [InlineData(@"foo\ ", "foo ", false, true)]
    [InlineData("a.b", "aXb", false, false)]
    public void Matches_FollowsGitSemantics(string pattern, string path, bool isDirectory, bool expected)
    {
        var rule = GitIgnoreRule.Parse(pattern, string.Empty);

        Assert.NotNull(rule);
        Assert.Equal(expected, rule.Matches(path, isDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# comment")]
    [InlineData("/")]
    [InlineData("!")]
    public void Parse_IgnoresBlankCommentAndEmptyPatterns(string line) =>
        Assert.Null(GitIgnoreRule.Parse(line, string.Empty));

    [Fact]
    public void Parse_RecognizesNegationAndDirectoryOnly()
    {
        var rule = GitIgnoreRule.Parse("!logs/", string.Empty)!;

        Assert.True(rule.IsNegated);
        Assert.True(rule.IsDirectoryOnly);
        Assert.Equal("logs", rule.Pattern);
    }

    [Theory]
    [InlineData("*.tmp", "src/x.tmp", true)]
    [InlineData("*.tmp", "src/a/x.tmp", true)]
    [InlineData("*.tmp", "x.tmp", false)]
    [InlineData("*.tmp", "srcx/x.tmp", false)]
    [InlineData("/gen", "src/gen", true)]
    [InlineData("/gen", "src/a/gen", false)]
    public void RuleInSubdirectory_IsRelativeToItsDirectory(string pattern, string path, bool expected) =>
        Assert.Equal(expected, GitIgnoreRule.Parse(pattern, "src")!.Matches(path, isDirectory: false));

    [Fact]
    public void CarriageReturn_IsIgnored() =>
        Assert.True(GitIgnoreRule.Parse("*.log\r", string.Empty)!.Matches("a.log", isDirectory: false));
}
