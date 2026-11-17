using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Application.Repositories;

/// <summary>
/// The only way any module reads repository content (SECURITY.md §3). Every call resolves the
/// path inside <see cref="RepositoryRoot"/>, refuses to leave the root (also via symbolic links or
/// junctions), and enforces a size limit.
/// </summary>
/// <remarks>
/// Expected problems are returned as errors with codes from <see cref="RepositoryErrorCodes"/>
/// (outside root, not found, too large, link not followed, unreadable). Cancellation throws
/// <see cref="OperationCanceledException"/>.
/// </remarks>
public interface IRepositoryFileReader
{
    /// <summary>Resolves and checks a file without reading it.</summary>
    Result<RepositoryFileStatus> GetStatus(RepositoryRoot root, RelativePath path);

    /// <summary>Reads at most <paramref name="maxBytes"/> bytes; larger files are not read.</summary>
    Task<Result<byte[]>> ReadAllBytesAsync(RepositoryRoot root, RelativePath path, long maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a text file of at most <paramref name="maxBytes"/> bytes. The encoding is detected from a
    /// byte order mark; without one, UTF-8 is assumed and invalid sequences are replaced.
    /// </summary>
    Task<Result<string>> ReadAllTextAsync(RepositoryRoot root, RelativePath path, long maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a checked file for sequential reading (e.g. hashing large files). The caller disposes
    /// the stream; no size limit applies.
    /// </summary>
    Result<Stream> OpenRead(RepositoryRoot root, RelativePath path);
}
