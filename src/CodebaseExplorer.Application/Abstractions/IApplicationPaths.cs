namespace CodebaseExplorer.Application.Abstractions;

/// <summary>
/// Well-known local storage locations of Codebase Explorer. All repository data is stored
/// below <see cref="DataDirectory"/> (local-first, see docs/architecture/SECURITY.md).
/// </summary>
public interface IApplicationPaths
{
    /// <summary>Root directory for all locally persisted application data.</summary>
    string DataDirectory { get; }

    /// <summary>Path of the SQLite database file that stores analysis results.</summary>
    string DatabaseFilePath { get; }

    /// <summary>Directory into which remote repositories are cloned.</summary>
    string ClonesDirectory { get; }

    /// <summary>Directory for application log files.</summary>
    string LogsDirectory { get; }
}
