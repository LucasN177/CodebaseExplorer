namespace CodebaseExplorer.Application.Git;

/// <summary>
/// A personal access token for an HTTPS remote, held in memory only (OPD-03 is open: tokens are never persisted).
/// <see cref="ToString"/> never reveals the secret, so the token cannot end up in logs or messages by accident.
/// </summary>
public sealed class GitAccessToken
{
    public GitAccessToken(string userName, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        UserName = userName;
        Secret = secret.Trim();
    }

    /// <summary>User name sent with the token (providers ignore or require a fixed value, see <see cref="For"/>).</summary>
    public string UserName { get; }

    /// <summary>The secret; pass it only to the credentials callback of the Git adapter.</summary>
    public string Secret { get; }

    /// <summary>
    /// A token for the given remote: GitLab expects the user name <c>oauth2</c>, GitHub and most other hosts accept
    /// <c>x-access-token</c> with a personal access token as password.
    /// </summary>
    public static GitAccessToken For(GitCloneUrl url, string secret)
    {
        ArgumentNullException.ThrowIfNull(url);
        return new GitAccessToken(url.SourceKind == Domain.Repositories.RepositorySourceKind.GitLab ? "oauth2" : "x-access-token", secret);
    }

    public override string ToString() => "GitAccessToken(***)";
}
