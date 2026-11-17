using CodebaseExplorer.Application.Abstractions;

namespace CodebaseExplorer.Infrastructure.FileSystem;

/// <summary>
/// Resolves the per-user data locations of Codebase Explorer.
/// </summary>
/// <remarks>
/// The default root is <see cref="Environment.SpecialFolder.LocalApplicationData"/>, which .NET maps to
/// <c>%LOCALAPPDATA%</c> on Windows, <c>~/Library/Application Support</c> on macOS and
/// <c>$XDG_DATA_HOME</c> (or <c>~/.local/share</c>) on Linux. This type only computes paths;
/// it does not create directories.
/// </remarks>
public sealed class ApplicationPaths : IApplicationPaths
{
    public const string ApplicationFolderName = "CodebaseExplorer";

    public ApplicationPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            ApplicationFolderName))
    {
    }

    public ApplicationPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        DataDirectory = Path.GetFullPath(dataDirectory);
        DatabaseFilePath = Path.Combine(DataDirectory, "codebase-explorer.db");
        ClonesDirectory = Path.Combine(DataDirectory, "clones");
        LogsDirectory = Path.Combine(DataDirectory, "logs");
    }

    public string DataDirectory { get; }

    public string DatabaseFilePath { get; }

    public string ClonesDirectory { get; }

    public string LogsDirectory { get; }
}
