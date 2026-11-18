using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;

namespace CodebaseExplorer.Analysis.FileSystem;

/// <inheritdoc />
internal sealed class RepositoryRootValidator(IApplicationPaths applicationPaths) : IRepositoryRootValidator
{
    public Result<RepositoryRoot> Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return Error(ApplicationError.Validation(RepositoryErrorCodes.PathInvalid, "The repository path must be an absolute path."), path);
        }

        if (IsDevicePath(path))
        {
            return Error(ApplicationError.Validation(RepositoryErrorCodes.DevicePath,
                "Device paths and administrative shares cannot be analyzed."), path);
        }

        var fullPath = PathContainment.Normalize(path);

        if (File.Exists(fullPath))
        {
            return Error(ApplicationError.Validation(RepositoryErrorCodes.NotADirectory, "The repository path is a file, not a folder."), fullPath);
        }

        var directory = new DirectoryInfo(fullPath);
        if (!directory.Exists)
        {
            return Error(ApplicationError.NotFound(RepositoryErrorCodes.PathNotFound, "The repository folder does not exist."), fullPath);
        }

        if (IsFileSystemRoot(fullPath))
        {
            return Error(ApplicationError.Validation(RepositoryErrorCodes.FileSystemRoot,
                "A file-system root cannot be analyzed; select the repository folder."), fullPath);
        }

        // A root that is itself a symbolic link or junction is analyzed at its final target.
        string resolved;
        try
        {
            resolved = PathContainment.Normalize(directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Error(ApplicationError.NotFound(RepositoryErrorCodes.PathNotFound,
                "The repository folder link could not be resolved."), fullPath);
        }

        if (IsFileSystemRoot(resolved))
        {
            return Error(ApplicationError.Validation(RepositoryErrorCodes.FileSystemRoot,
                "A file-system root cannot be analyzed; select the repository folder."), resolved);
        }

        // App-managed clones (GIT-003) are the one kind of repository inside the data directory: a folder strictly
        // below the clones directory is allowed, the clones directory itself and everything else is not.
        var dataDirectory = PathContainment.Normalize(applicationPaths.DataDirectory);
        var clonesDirectory = PathContainment.Normalize(applicationPaths.ClonesDirectory);
        var isClone = PathContainment.IsWithin(resolved, clonesDirectory) && !string.Equals(resolved, clonesDirectory, PathContainment.Comparison);
        if (!isClone && (PathContainment.IsWithin(resolved, dataDirectory) || PathContainment.IsWithin(dataDirectory, resolved)))
        {
            return Error(ApplicationError.Validation(RepositoryErrorCodes.OverlapsAppData,
                "The folder contains or lies inside Codebase Explorer's own data directory."), resolved);
        }

        return Result<RepositoryRoot>.Success(new RepositoryRoot(resolved));
    }

    private static bool IsFileSystemRoot(string normalizedPath) =>
        string.Equals(
            normalizedPath,
            PathContainment.Normalize(Path.GetPathRoot(normalizedPath) ?? normalizedPath),
            PathContainment.Comparison);

    private static bool IsDevicePath(string path)
    {
        var unified = path.Replace('/', '\\');

        if (unified.StartsWith(@"\\.\", StringComparison.Ordinal) || unified.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return true;
        }

        // Administrative shares such as \\server\c$ or \\server\admin$.
        if (OperatingSystem.IsWindows() && unified.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var segments = unified.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 2 && segments[1].EndsWith('$');
        }

        return false;
    }

    private static Result<RepositoryRoot> Error(ApplicationError error, string path) =>
        Result<RepositoryRoot>.Failure(new ApplicationError(
            error.Kind, error.Code, error.Message, new Dictionary<string, string> { ["path"] = path }));
}
