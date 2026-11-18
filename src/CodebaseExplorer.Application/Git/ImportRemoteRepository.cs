using System.Collections.Concurrent;
using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Analysis;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Contracts.Repositories;
using CodebaseExplorer.Domain.Repositories;

namespace CodebaseExplorer.Application.Git;

/// <summary>
/// Use case: clone a GitHub/GitLab (or other HTTPS) remote into the app's clones directory and analyze it
/// (GIT-003, USER_WORKFLOWS.md WF-01, GIT_INTEGRATION.md §4–5). The repository is registered only after a complete
/// clone; a cancelled or failed clone leaves neither a directory nor a registration behind.
/// </summary>
public sealed class ImportRemoteRepository(IGitRemoteCloner cloner, IApplicationPaths applicationPaths, ImportLocalRepository importer)
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Result<RemoteRepositoryImportDto>> ExecuteAsync(
        RemoteImportRequest request,
        IProgress<GitProgress>? gitProgress,
        IProgress<AnalysisProgress>? analysisProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parsed = GitRemoteUrls.ParseCloneUrl(request.Url);
        if (!parsed.TryGetValue(out var url, out var urlError))
        {
            return urlError;
        }

        var target = Path.Combine([applicationPaths.ClonesDirectory, .. url.CloneSegments]);
        if (!_running.TryAdd(target, 0))
        {
            return ApplicationError.Conflict(GitErrorCodes.CloneInProgress, "This repository is already being cloned.",
                new Dictionary<string, string> { ["url"] = url.Url.AbsoluteUri });
        }

        try
        {
            var token = string.IsNullOrWhiteSpace(request.Token) ? null : GitAccessToken.For(url, request.Token);
            var branch = string.IsNullOrWhiteSpace(request.Branch) ? null : request.Branch.Trim();

            var cloned = await CloneOrUpdateAsync(url, target, branch, token, request.ExistingClone, gitProgress, cancellationToken);
            if (!cloned.TryGetValue(out var clone, out var cloneError))
            {
                return cloneError;
            }

            var imported = await importer.ExecuteAsync(
                clone.Clone.Path,
                root => RepositoryLocation.Cloned(root.FullPath, url.Url, url.SourceKind),
                analysisProgress,
                cancellationToken);

            return imported.Map(summary => new RemoteRepositoryImportDto
            {
                Summary = summary,
                RemoteUrl = url.Url.AbsoluteUri,
                ClonePath = clone.Clone.Path,
                Branch = clone.Clone.Branch,
                Updated = clone.Updated,
                CredentialsRemovedFromUrl = url.CredentialsRemoved,
                HasSubmodules = clone.Clone.HasSubmodules,
            });
        }
        finally
        {
            _running.TryRemove(target, out _);
        }
    }

    private async Task<Result<(GitCloneResult Clone, bool Updated)>> CloneOrUpdateAsync(
        GitCloneUrl url,
        string target,
        string? branch,
        GitAccessToken? token,
        ExistingCloneAction existing,
        IProgress<GitProgress>? progress,
        CancellationToken cancellationToken)
    {
        var request = new GitCloneRequest(url.Url, target, branch, token);
        var cloned = await cloner.CloneAsync(request, progress, cancellationToken);
        if (cloned.IsSuccess || cloned.Error.Code != GitErrorCodes.CloneExists || existing == ExistingCloneAction.Ask)
        {
            return cloned.Map(c => (c, false));
        }

        if (existing == ExistingCloneAction.Update)
        {
            var updated = await cloner.UpdateAsync(target, branch, token, progress, cancellationToken);
            return updated.Map(c => (c, true));
        }

        var deleted = await cloner.DeleteCloneAsync(target, cancellationToken);
        if (deleted.IsFailure)
        {
            return deleted.Error;
        }

        var recloned = await cloner.CloneAsync(request, progress, cancellationToken);
        return recloned.Map(c => (c, false));
    }
}

/// <summary>Input of <see cref="ImportRemoteRepository"/>.</summary>
/// <param name="Url">HTTPS address entered by the user.</param>
/// <param name="Branch">Branch to analyze; the remote's default branch if empty.</param>
/// <param name="Token">Personal access token for private repositories; kept in memory only (OPD-03).</param>
/// <param name="ExistingClone">What to do when the repository was cloned before.</param>
public sealed record RemoteImportRequest(string Url, string? Branch, string? Token, ExistingCloneAction ExistingClone)
{
    /// <summary>Never prints the token.</summary>
    public override string ToString() =>
        $"RemoteImportRequest {{ Url = {GitRemoteUrls.Sanitize(Url)}, Branch = {Branch}, Token = {(Token is null ? "none" : "***")}, ExistingClone = {ExistingClone} }}";
}

/// <summary>Handling of an existing clone (GIT_INTEGRATION.md §4: "the user chooses update or re-clone").</summary>
public enum ExistingCloneAction
{
    /// <summary>Return <see cref="GitErrorCodes.CloneExists"/> so that the user can choose.</summary>
    Ask,

    /// <summary>Fetch and check out the branch in the existing clone.</summary>
    Update,

    /// <summary>Delete the existing clone and clone again.</summary>
    Reclone,
}
