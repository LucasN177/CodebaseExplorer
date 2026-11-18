namespace CodebaseExplorer.Application.Git;

/// <summary>Stable <see cref="Common.ApplicationError"/> codes of the Git integration (GIT_INTEGRATION.md).</summary>
public static class GitErrorCodes
{
    public const string NotARepository = "git.not-a-repository";
    public const string ReadFailed = "git.read-failed";

    public const string UrlInvalid = "git.url-invalid";
    public const string UrlSchemeNotSupported = "git.url-scheme-not-supported";
    public const string ClonePathInvalid = "git.clone-path-invalid";
    public const string CloneExists = "git.clone-exists";
    public const string CloneInProgress = "git.clone-in-progress";
    public const string CloneFailed = "git.clone-failed";
    public const string AuthenticationFailed = "git.authentication-failed";
    public const string BranchNotFound = "git.branch-not-found";
}
