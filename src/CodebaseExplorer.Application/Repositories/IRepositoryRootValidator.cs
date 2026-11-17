using CodebaseExplorer.Application.Common;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>Validates a user-selected folder as a repository root (ANALYSIS_ENGINE.md Stage 0).</summary>
public interface IRepositoryRootValidator
{
    /// <summary>
    /// Returns the canonical root, or a <see cref="ErrorKind.Validation"/>/<see cref="ErrorKind.NotFound"/>
    /// error (codes in <see cref="RepositoryErrorCodes"/>) if the path does not exist, is not a
    /// directory, is a file-system root or device path, or overlaps the application data directory.
    /// </summary>
    Result<RepositoryRoot> Validate(string path);
}
