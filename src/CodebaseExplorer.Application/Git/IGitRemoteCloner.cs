using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.Git;

/// <summary>
/// Clones remotes into, updates and deletes app-managed clones (GIT_INTEGRATION.md §2, §4). Every path must lie inside
/// <see cref="Abstractions.IApplicationPaths.ClonesDirectory"/>; the app never fetches into or deletes user-managed
/// working trees. Submodules are never cloned.
/// </summary>
public interface IGitRemoteCloner
{
    /// <summary>
    /// Clones <see cref="GitCloneRequest.RemoteUrl"/> with full history into <see cref="GitCloneRequest.TargetPath"/>.
    /// On failure or cancellation (<see cref="OperationCanceledException"/>) nothing is left at the target.
    /// </summary>
    /// <returns>
    /// The clone; <see cref="GitErrorCodes.CloneExists"/> (Conflict) if the target exists,
    /// <see cref="GitErrorCodes.AuthenticationFailed"/>, <see cref="GitErrorCodes.BranchNotFound"/> or
    /// <see cref="GitErrorCodes.CloneFailed"/>.
    /// </returns>
    Task<Result<GitCloneResult>> CloneAsync(GitCloneRequest request, IProgress<GitProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches <c>origin</c> and checks out <paramref name="branch"/> (default: the current branch) at the fetched commit,
    /// discarding local changes — app-managed clones are never edited by the user.
    /// </summary>
    Task<Result<GitCloneResult>> UpdateAsync(
        string clonePath, string? branch, GitAccessToken? token, IProgress<GitProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Deletes an app-managed clone directory (no-op if it does not exist).</summary>
    Task<Result> DeleteCloneAsync(string clonePath, CancellationToken cancellationToken);
}

/// <summary>What <see cref="IGitRemoteCloner.CloneAsync"/> clones.</summary>
/// <param name="RemoteUrl">The remote (validated by the caller; the product passes only <see cref="GitCloneUrl.Url"/>).</param>
/// <param name="TargetPath">Absolute clone directory inside the clones directory.</param>
/// <param name="Branch">Branch to check out; the remote's default branch if <see langword="null"/>.</param>
/// <param name="Token">Credentials for private repositories, passed only through the credentials callback.</param>
public sealed record GitCloneRequest(Uri RemoteUrl, string TargetPath, string? Branch, GitAccessToken? Token);

/// <summary>A clone after cloning or updating.</summary>
/// <param name="Path">Absolute clone directory.</param>
/// <param name="Branch">Checked-out branch.</param>
/// <param name="HeadCommitSha">Checked-out commit.</param>
/// <param name="HasSubmodules">The repository declares submodules (<c>.gitmodules</c>); they were not cloned.</param>
public sealed record GitCloneResult(string Path, string? Branch, string? HeadCommitSha, bool HasSubmodules);
