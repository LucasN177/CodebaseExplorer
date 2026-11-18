using CodebaseExplorer.Domain.Repositories;

namespace CodebaseExplorer.Application.Git;

/// <summary>
/// A validated remote for cloning (GIT_INTEGRATION.md §4), created by <see cref="GitRemoteUrls.ParseCloneUrl"/>:
/// HTTPS only, no credentials, no query or fragment, safe path segments.
/// </summary>
/// <param name="Url">The remote URL without credentials; this is what is stored and shown.</param>
/// <param name="SourceKind"><see cref="RepositorySourceKind.GitHub"/> for github.com, <see cref="RepositorySourceKind.GitLab"/> for gitlab.com, otherwise <see cref="RepositorySourceKind.OtherGitRemote"/>.</param>
/// <param name="CloneSegments">
/// Relative clone directory below the clones directory: host (with <c>_port</c> for a non-default port), the owner
/// path (one or more segments, e.g. GitLab subgroups) and the repository name — lowercase, without <c>.git</c>.
/// </param>
/// <param name="CredentialsRemoved">The input contained user information (<c>user:token@</c>) that was dropped; the user should be warned.</param>
public sealed record GitCloneUrl(Uri Url, RepositorySourceKind SourceKind, IReadOnlyList<string> CloneSegments, bool CredentialsRemoved)
{
    /// <summary>The repository name (last clone segment).</summary>
    public string Name => CloneSegments[^1];
}
