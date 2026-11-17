namespace CodebaseExplorer.TestRepositories;

/// <summary>
/// A uniquely named temporary directory that is deleted on dispose (TEST_STRATEGY.md §6).
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        FullPath = Path.Combine(Path.GetTempPath(), "codebase-explorer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    /// <summary>Absolute path of a repository-relative path (using <c>/</c> separators).</summary>
    public string PathOf(string relativePath) =>
        Path.Combine(FullPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Writes a UTF-8 text file (without BOM), creating parent directories.</summary>
    public string WriteText(string relativePath, string content)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Writes a binary file, creating parent directories.</summary>
    public string WriteBytes(string relativePath, byte[] content)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Creates a directory, including parents.</summary>
    public string CreateDirectory(string relativePath) => Directory.CreateDirectory(PathOf(relativePath)).FullName;

    /// <summary>
    /// Tries to create a symbolic link; returns <see langword="false"/> if the platform or account does
    /// not allow it (e.g. Windows without developer mode), so the caller can skip with a reason.
    /// </summary>
    public bool TryCreateSymbolicLink(string relativeLinkPath, string targetPath, bool isDirectory)
    {
        var linkPath = PathOf(relativeLinkPath);
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        try
        {
            if (isDirectory)
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
            }
            else
            {
                File.CreateSymbolicLink(linkPath, targetPath);
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a Windows directory junction (needs no special privileges, unlike symbolic links).
    /// Returns <see langword="false"/> on other platforms or if creation fails.
    /// </summary>
    /// <remarks>Uses <c>cmd /c mklink /J</c>; test-only, never used by the product.</remarks>
    public bool TryCreateJunction(string relativeLinkPath, string targetDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var linkPath = PathOf(relativeLinkPath);
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetDirectory);

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        process.WaitForExit(10_000);
        return process.HasExited && process.ExitCode == 0 && Directory.Exists(linkPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(FullPath))
            {
                // Do not descend through links/junctions: their targets may lie elsewhere.
                var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(FullPath, "*", options))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(FullPath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp directory must not fail a test run.
        }
    }
}
