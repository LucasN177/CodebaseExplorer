namespace CodebaseExplorer.Application.Repositories;

/// <summary>Stable <see cref="Common.ApplicationError"/> codes for repository roots and file access.</summary>
public static class RepositoryErrorCodes
{
    public const string PathInvalid = "repository.path-invalid";
    public const string PathNotFound = "repository.path-not-found";
    public const string NotADirectory = "repository.not-a-directory";
    public const string FileSystemRoot = "repository.file-system-root";
    public const string DevicePath = "repository.device-path";
    public const string OverlapsAppData = "repository.overlaps-app-data";
    public const string ImportInProgress = "repository.import-in-progress";
    public const string NotAnalyzed = "repository.not-analyzed";
    public const string IdInvalid = "repository.id-invalid";

    public const string FileOutsideRoot = "repository-file.outside-root";
    public const string FileNotFound = "repository-file.not-found";
    public const string FileTooLarge = "repository-file.too-large";
    public const string FileLinkNotFollowed = "repository-file.link-not-followed";
    public const string FileUnreadable = "repository-file.unreadable";
}
