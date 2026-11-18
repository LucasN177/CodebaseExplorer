using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Git;
using LibGit2Sharp;

namespace CodebaseExplorer.Git.Cloning;

/// <summary>
/// <see cref="IGitRemoteCloner"/> on LibGit2Sharp (GIT-003). A clone is written to <c>&lt;target&gt;.partial</c> and moved to
/// the target only when it is complete, so a failed, cancelled or interrupted clone never looks like a clone. Tokens
/// reach libgit2 only through the credentials callback and are never written to <c>.git/config</c>.
/// </summary>
internal sealed class LibGit2RemoteCloner(IApplicationPaths applicationPaths) : IGitRemoteCloner
{
    private const string PartialSuffix = ".partial";
    private const string RemoteName = "origin";

    public Task<Result<GitCloneResult>> CloneAsync(GitCloneRequest request, IProgress<GitProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Clone(request, progress, cancellationToken), cancellationToken);
    }

    public Task<Result<GitCloneResult>> UpdateAsync(
        string clonePath, string? branch, GitAccessToken? token, IProgress<GitProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clonePath);
        return Task.Run(() => Update(clonePath, branch, token, progress, cancellationToken), cancellationToken);
    }

    public Task<Result> DeleteCloneAsync(string clonePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clonePath);
        return Task.Run(() =>
        {
            if (!TryResolveClonePath(clonePath, out var path, out var error))
            {
                return Result.Failure(error);
            }

            DirectoryRemover.Delete(path);
            DirectoryRemover.Delete(path + PartialSuffix);
            return Result.Success();
        }, cancellationToken);
    }

    private Result<GitCloneResult> Clone(GitCloneRequest request, IProgress<GitProgress>? progress, CancellationToken cancellationToken)
    {
        if (!TryResolveClonePath(request.TargetPath, out var target, out var pathError))
        {
            return pathError;
        }

        if (Directory.Exists(target) || File.Exists(target))
        {
            return ApplicationError.Conflict(GitErrorCodes.CloneExists, "A clone of this repository already exists.",
                new Dictionary<string, string> { ["path"] = target });
        }

        var partial = target + PartialSuffix;
        DirectoryRemover.Delete(partial); // left over from an interrupted earlier clone
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var options = new CloneOptions
        {
            BranchName = request.Branch,
            Checkout = true,
            RecurseSubmodules = false,
            OnCheckoutProgress = (_, completed, total) => progress?.Report(new GitProgress(GitProgressStage.CheckingOut, completed, total)),
        };
        Configure(options.FetchOptions, request.Token, progress, cancellationToken);

        try
        {
            Repository.Clone(request.RemoteUrl.OriginalString, partial, options);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(partial, target);
            return Describe(target);
        }
        catch (Exception exception) when (exception is UserCancelledException or OperationCanceledException)
        {
            DirectoryRemover.Delete(partial);
            throw new OperationCanceledException("The clone was cancelled.", exception, cancellationToken);
        }
        catch (LibGit2SharpException exception)
        {
            DirectoryRemover.Delete(partial);
            return Map(exception, request.Branch, "The repository could not be cloned");
        }
        catch (IOException exception)
        {
            DirectoryRemover.Delete(partial);
            return ApplicationError.Failure(GitErrorCodes.CloneFailed, $"The repository could not be cloned: {exception.Message}");
        }
    }

    private Result<GitCloneResult> Update(
        string clonePath, string? branch, GitAccessToken? token, IProgress<GitProgress>? progress, CancellationToken cancellationToken)
    {
        if (!TryResolveClonePath(clonePath, out var path, out var pathError))
        {
            return pathError;
        }

        if (!Directory.Exists(Path.Combine(path, ".git")))
        {
            return ApplicationError.NotFound(GitErrorCodes.NotARepository, "The clone no longer exists.",
                new Dictionary<string, string> { ["path"] = path });
        }

        try
        {
            using var repository = new Repository(path);
            var remote = repository.Network.Remotes[RemoteName];
            if (remote is null)
            {
                return ApplicationError.Failure(GitErrorCodes.CloneFailed, "The clone has no 'origin' remote.");
            }

            var fetchOptions = new FetchOptions { Prune = true };
            Configure(fetchOptions, token, progress, cancellationToken);
            Commands.Fetch(repository, RemoteName, remote.FetchRefSpecs.Select(r => r.Specification), fetchOptions, logMessage: null);
            cancellationToken.ThrowIfCancellationRequested();

            var branchName = branch ?? (repository.Info.IsHeadDetached ? null : repository.Head.FriendlyName);
            var remoteBranch = branchName is null ? null : repository.Branches[$"{RemoteName}/{branchName}"];
            if (branchName is null || remoteBranch?.Tip is null)
            {
                return ApplicationError.NotFound(GitErrorCodes.BranchNotFound, $"The branch '{branchName}' does not exist on the remote.");
            }

            var local = repository.Branches[branchName] ?? repository.CreateBranch(branchName, remoteBranch.Tip);
            repository.Branches.Update(local, b => b.TrackedBranch = remoteBranch.CanonicalName);
            Commands.Checkout(repository, local, new CheckoutOptions
            {
                CheckoutModifiers = CheckoutModifiers.Force,
                OnCheckoutProgress = (_, completed, total) => progress?.Report(new GitProgress(GitProgressStage.CheckingOut, completed, total)),
            });
            repository.Reset(ResetMode.Hard, remoteBranch.Tip);
            return Describe(path);
        }
        catch (Exception exception) when (exception is UserCancelledException or OperationCanceledException)
        {
            // The clone stays a complete repository; only the fetch was interrupted.
            throw new OperationCanceledException("The update was cancelled.", exception, cancellationToken);
        }
        catch (LibGit2SharpException exception)
        {
            return Map(exception, branch, "The clone could not be updated");
        }
    }

    private static void Configure(FetchOptions options, GitAccessToken? token, IProgress<GitProgress>? progress, CancellationToken cancellationToken)
    {
        options.Depth = 0; // full history: activity statistics need it (GIT-006)
        options.OnTransferProgress = transfer =>
        {
            progress?.Report(new GitProgress(GitProgressStage.Receiving, transfer.ReceivedObjects, transfer.TotalObjects));
            return !cancellationToken.IsCancellationRequested;
        };

        var attempts = 0;
        options.CredentialsProvider = (_, _, _) =>
        {
            // libgit2 asks again after a rejected credential; answering twice would loop on a wrong token.
            if (token is null || Interlocked.Increment(ref attempts) > 1)
            {
                throw new AuthenticationRejectedException();
            }

            return new UsernamePasswordCredentials { Username = token.UserName, Password = token.Secret };
        };
    }

    private static GitCloneResult Describe(string path)
    {
        using var repository = new Repository(path);
        return new GitCloneResult(
            path,
            repository.Info.IsHeadDetached ? null : repository.Head.FriendlyName,
            repository.Head.Tip?.Sha,
            HasSubmodules: File.Exists(Path.Combine(path, ".gitmodules")));
    }

    private static ApplicationError Map(LibGit2SharpException exception, string? branch, string action)
    {
        // Messages of libgit2 contain the URL as passed (never with credentials) and HTTP status details.
        if (exception is AuthenticationRejectedException
            || exception.InnerException is AuthenticationRejectedException
            || exception.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("401", StringComparison.Ordinal)
            || exception.Message.Contains("403", StringComparison.Ordinal))
        {
            return ApplicationError.Failure(GitErrorCodes.AuthenticationFailed,
                "The remote rejected the request. For a private repository, enter a valid access token.");
        }

        if (branch is not null && exception is NotFoundException)
        {
            return ApplicationError.NotFound(GitErrorCodes.BranchNotFound, $"The branch '{branch}' does not exist on the remote.");
        }

        return ApplicationError.Failure(GitErrorCodes.CloneFailed, $"{action}: {exception.Message}");
    }

    /// <summary>Accepts only absolute paths strictly inside the clones directory (defense in depth for delete).</summary>
    private bool TryResolveClonePath(string clonePath, out string path, out ApplicationError error)
    {
        var clones = Normalize(applicationPaths.ClonesDirectory);
        path = Path.IsPathFullyQualified(clonePath) ? Normalize(clonePath) : string.Empty;
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (path.Length > clones.Length + 1
            && path.StartsWith(clones + Path.DirectorySeparatorChar, comparison)
            && !path.EndsWith(PartialSuffix, comparison))
        {
            error = null!;
            return true;
        }

        error = ApplicationError.Validation(GitErrorCodes.ClonePathInvalid, "Clones are stored only inside the app's clones directory.",
            new Dictionary<string, string> { ["path"] = clonePath });
        return false;
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed class AuthenticationRejectedException() : LibGit2SharpException("Authentication was rejected.");
}
