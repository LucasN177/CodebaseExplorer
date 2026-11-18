using CodebaseExplorer.Application.Git;
using CodebaseExplorer.Domain.Repositories;

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

    [Theory]
    [InlineData("https://github.com/Owner/Repo", "https://github.com/Owner/Repo", RepositorySourceKind.GitHub, "github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo.git", RepositorySourceKind.GitHub, "github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo/", "https://github.com/owner/repo", RepositorySourceKind.GitHub, "github.com/owner/repo")]
    [InlineData("HTTPS://GitLab.com/group/sub/repo.git", "https://gitlab.com/group/sub/repo.git", RepositorySourceKind.GitLab, "gitlab.com/group/sub/repo")]
    [InlineData("https://git.example.org:8443/team/.github", "https://git.example.org:8443/team/.github", RepositorySourceKind.OtherGitRemote, "git.example.org_8443/team/.github")]
    [InlineData("https://gitlab.com:443/a/b", "https://gitlab.com/a/b", RepositorySourceKind.GitLab, "gitlab.com/a/b")]
    public void ParseCloneUrl_AcceptsHttpsRemotes(string input, string url, RepositorySourceKind kind, string cloneSegments)
    {
        var parsed = GitRemoteUrls.ParseCloneUrl(input).Value;

        Assert.Equal(url, parsed.Url.AbsoluteUri);
        Assert.Equal(kind, parsed.SourceKind);
        Assert.Equal(cloneSegments, string.Join('/', parsed.CloneSegments));
        Assert.False(parsed.CredentialsRemoved);
    }

    [Fact]
    public void ParseCloneUrl_RemovesAndReportsCredentials()
    {
        var parsed = GitRemoteUrls.ParseCloneUrl("https://me:ghp_secret@github.com/owner/repo").Value;

        Assert.Equal("https://github.com/owner/repo", parsed.Url.AbsoluteUri);
        Assert.Empty(parsed.Url.UserInfo);
        Assert.True(parsed.CredentialsRemoved);
    }

    [Theory]
    [InlineData("http://github.com/owner/repo")]
    [InlineData("ssh://git@github.com/owner/repo.git")]
    [InlineData("git://github.com/owner/repo.git")]
    [InlineData("file:///C:/repos/repo.git")]
    [InlineData("file:///srv/repo.git")]
    [InlineData("ftp://example.com/owner/repo")]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("github.com/owner/repo")]
    [InlineData(@"C:\repos\repo")]
    [InlineData("/srv/git/repo.git")]
    [InlineData("ext::sh -c touch% /tmp/pwned")]
    public void ParseCloneUrl_RejectsOtherSchemes(string input)
    {
        Assert.Equal(GitErrorCodes.UrlSchemeNotSupported, GitRemoteUrls.ParseCloneUrl(input).Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/owner/repo/tree/main")]
    [InlineData("https://github.com/owner/repo?ref=main")]
    [InlineData("https://github.com/owner/repo#readme")]
    [InlineData("https://example.com/owner/..")]
    [InlineData("https://example.com/owner/repo.")]
    [InlineData("https://example.com/own%20er/repo")]
    [InlineData("https://example.com/owner/re$po")]
    public void ParseCloneUrl_RejectsInvalidAddresses(string input)
    {
        Assert.Equal(GitErrorCodes.UrlInvalid, GitRemoteUrls.ParseCloneUrl(input).Error.Code);
    }

    [Fact]
    public void AccessToken_NeverPrintsTheSecret()
    {
        var gitLab = GitRemoteUrls.ParseCloneUrl("https://gitlab.com/a/b").Value;
        var gitHub = GitRemoteUrls.ParseCloneUrl("https://github.com/a/b").Value;

        var token = GitAccessToken.For(gitLab, "glpat-secret");

        Assert.Equal("oauth2", token.UserName);
        Assert.Equal("x-access-token", GitAccessToken.For(gitHub, "ghp_secret").UserName);
        Assert.DoesNotContain("secret", token.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", new RemoteImportRequest("https://u:p@github.com/a/b", null, "ghp_secret", ExistingCloneAction.Ask).ToString(), StringComparison.Ordinal);
    }
}
