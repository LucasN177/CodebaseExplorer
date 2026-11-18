using System.Text;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>Writes an exported report into a folder the user chose (EXPORT-002). Keeps file I/O out of ViewModels.</summary>
public interface IReportFileWriter
{
    /// <summary>
    /// Writes <paramref name="content"/> as UTF-8 (without BOM) to <paramref name="fileName"/> in <paramref name="folder"/>
    /// (created if missing). An existing file is never overwritten: <c>name (2).md</c>, <c>name (3).md</c>, … are used.
    /// </summary>
    /// <returns>The absolute path of the written file.</returns>
    Task<string> WriteAsync(string folder, string fileName, string content, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class ReportFileWriter : IReportFileWriter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<string> WriteAsync(string folder, string fileName, string content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        if (Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("The file name must not contain a path.", nameof(fileName));
        }

        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var attempt = 1; ; attempt++)
        {
            var path = Path.Combine(folder, attempt == 1 ? fileName : $"{stem} ({attempt}){extension}");
            try
            {
                // CreateNew: never overwrite, also not a file created between check and write.
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
                await using var writer = new StreamWriter(stream, Utf8);
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                return path;
            }
            catch (IOException) when (File.Exists(path) && attempt < 1000)
            {
                // Taken: try the next number.
            }
        }
    }

    /// <summary>
    /// True if <paramref name="folder"/> is <paramref name="repositoryRoot"/> or lies inside it (case-insensitive on Windows
    /// and macOS). Writing there needs the user's explicit confirmation (SECURITY.md S5).
    /// </summary>
    public static bool IsInsideRepository(string folder, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var target = Normalize(folder);
        var root = Normalize(repositoryRoot);
        return string.Equals(target, root, comparison) || target.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
}
