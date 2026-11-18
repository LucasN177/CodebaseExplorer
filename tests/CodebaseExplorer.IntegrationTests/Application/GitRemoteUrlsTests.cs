using CodebaseExplorer.Application.Git;

namespace CodebaseExplorer.IntegrationTests.Application;

/// <summary>GIT-001: remote URLs never keep credentials (GIT_INTEGRATION.md §4, SECURITY.md "Credentials").</summary>
public sealed class GitRemoteUrlsTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo.git")]
    [InlineData("https://user:ghp_secret@github.com/owner/repo.git", "https://github.com/owner/repo.git")]
    [InlineData("https://oauth2:glpat-secret@gitlab.example.com:8443/group/sub/repo", "https://gitlab.example.com:8443/group/sub/repo")]
    [InlineData("https://github.com/owner/repo.git?access_token=secret#frag", "https://github.com/owner/repo.git")]
    [InlineData("HTTPS://GitHub.com/Owner/Repo", "https://github.com/Owner/Repo")]
    [InlineData("https://github.com:443/owner/repo", "https://github.com/owner/repo")]
    [InlineData("ssh://git:secret@host.example/owner/repo.git", "ssh://host.example/owner/repo.git")]
    [InlineData("git@github.com:owner/repo.git", "github.com:owner/repo.git")]
    [InlineData("token@github.com:owner/repo.git", "github.com:owner/repo.git")]
    [InlineData("github.com:owner/repo.git", "github.com:owner/repo.git")]
    [InlineData("../relative/repo", "../relative/repo")]
    [InlineData("/srv/git/repo.git", "/srv/git/repo.git")]
    [InlineData(@"C:\repos\x.git", @"C:\repos\x.git")]
    [InlineData("  https://github.com/owner/repo  ", "https://github.com/owner/repo")]
    public void Sanitize_RemovesCredentials(string url, string expected)
    {
        Assert.Equal(expected, GitRemoteUrls.Sanitize(url));
    }

    [Fact]
    public void Sanitize_ReplacesUnparseableUrlsWithScheme()
    {
        Assert.Equal("https://<invalid>", GitRemoteUrls.Sanitize("https://user:secret@[bad/host"));
    }
}
