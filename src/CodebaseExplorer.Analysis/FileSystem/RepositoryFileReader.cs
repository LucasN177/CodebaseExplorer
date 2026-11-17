using System.Text;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.FileSystem;

/// <summary>
/// Containment-checking reader for repository files (SECURITY.md §3). Each path segment below the
/// root is inspected for symbolic links/junctions; the resolved file must lie inside the root.
/// </summary>
/// <remarks>
/// The check runs immediately before the file is opened. A repository that is modified concurrently
/// (a segment replaced by a link between check and open) is not defended against; repositories are
/// analyzed as they lie on disk and are not expected to change during analysis.
/// </remarks>
internal sealed class RepositoryFileReader(SymbolicLinkPolicy linkPolicy = SymbolicLinkPolicy.DoNotFollow) : IRepositoryFileReader
{
    private const int BufferSize = 81920;

    public Result<RepositoryFileStatus> GetStatus(RepositoryRoot root, RelativePath path) =>
        Resolve(root, path).Map(file => new RepositoryFileStatus(path, file.Length, file.LastWriteTimeUtc));

    public async Task<Result<byte[]>> ReadAllBytesAsync(
        RepositoryRoot root, RelativePath path, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var resolved = Resolve(root, path);
        if (!resolved.TryGetValue(out var file, out var error))
        {
            return error;
        }

        if (file.Length > maxBytes)
        {
            return TooLarge(path, file.Length, maxBytes);
        }

        try
        {
            await using var stream = OpenStream(file.FullName);
            var buffer = new MemoryStream((int)Math.Min(file.Length, maxBytes));
            var chunk = new byte[(int)Math.Min(BufferSize, maxBytes + 1)];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    // The file grew after the size check.
                    return TooLarge(path, buffer.Length + read, maxBytes);
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Unreadable(path, exception);
        }
    }

    public async Task<Result<string>> ReadAllTextAsync(
        RepositoryRoot root, RelativePath path, long maxBytes, CancellationToken cancellationToken)
    {
        var bytes = await ReadAllBytesAsync(root, path, maxBytes, cancellationToken);
        return bytes.Map(Decode);
    }

    public Result<Stream> OpenRead(RepositoryRoot root, RelativePath path)
    {
        var resolved = Resolve(root, path);
        if (!resolved.TryGetValue(out var file, out var error))
        {
            return error;
        }

        try
        {
            return OpenStream(file.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Unreadable(path, exception);
        }
    }

    private Result<FileInfo> Resolve(RepositoryRoot root, RelativePath path)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (path.IsRoot)
        {
            return Failure(RepositoryErrorCodes.FileNotFound, "The repository root is not a file.", path);
        }

        var candidate = Path.GetFullPath(Path.Join(root.FullPath, path.Value));
        if (!PathContainment.IsWithin(candidate, root.FullPath))
        {
            return Failure(RepositoryErrorCodes.FileOutsideRoot, "The path resolves outside the repository.", path);
        }

        try
        {
            var current = root.FullPath;
            foreach (var segment in path.Value.Split('/'))
            {
                current = Path.Join(current, segment);

                FileSystemInfo info = new FileInfo(current);
                if (!info.Exists)
                {
                    info = new DirectoryInfo(current);
                }

                if (info.LinkTarget is not null)
                {
                    if (linkPolicy == SymbolicLinkPolicy.DoNotFollow)
                    {
                        return Failure(RepositoryErrorCodes.FileLinkNotFollowed,
                            "The path goes through a symbolic link or junction, which is not followed.", path);
                    }

                    var target = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (target is null || !target.Exists)
                    {
                        return Failure(RepositoryErrorCodes.FileNotFound, "The link target does not exist.", path);
                    }

                    if (!PathContainment.IsWithin(target.FullName, root.FullPath))
                    {
                        return Failure(RepositoryErrorCodes.FileOutsideRoot, "A link on the path points outside the repository.", path);
                    }

                    current = target.FullName;
                    continue;
                }

                if (!info.Exists)
                {
                    return Failure(RepositoryErrorCodes.FileNotFound, "The file does not exist.", path);
                }
            }

            var file = new FileInfo(current);
            return file.Exists
                ? file
                : Failure(RepositoryErrorCodes.FileNotFound, "The path is not a file.", path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Unreadable(path, exception);
        }
    }

    private static FileStream OpenStream(string fullPath) =>
        new(fullPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = BufferSize,
        });

    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, throwOnInvalidBytes: false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static ApplicationError TooLarge(RelativePath path, long size, long maxBytes) =>
        new(ErrorKind.Validation, RepositoryErrorCodes.FileTooLarge, "The file exceeds the size limit and was not read.",
            new Dictionary<string, string>
            {
                ["path"] = path.Value,
                ["sizeBytes"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["maxBytes"] = maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

    private static ApplicationError Unreadable(RelativePath path, Exception exception) =>
        new(ErrorKind.Failure, RepositoryErrorCodes.FileUnreadable, "The file could not be read.",
            new Dictionary<string, string> { ["path"] = path.Value, ["reason"] = exception.GetType().Name });

    private static ApplicationError Failure(string code, string message, RelativePath path) =>
        new(code == RepositoryErrorCodes.FileNotFound ? ErrorKind.NotFound : ErrorKind.Validation, code, message,
            new Dictionary<string, string> { ["path"] = path.ToString() });
}
