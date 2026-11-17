namespace CodebaseExplorer.Analysis.Scanning;

/// <summary>Counts of what the scanner included and skipped.</summary>
internal sealed record ScanStatistics
{
    public int FilesScanned { get; init; }

    /// <summary>Files excluded by <c>.gitignore</c> rules.</summary>
    public int FilesIgnored { get; init; }

    /// <summary>Directories skipped because their name is a built-in exclude (e.g. <c>bin</c>, <c>node_modules</c>).</summary>
    public int DirectoriesExcluded { get; init; }

    /// <summary>Directories excluded by <c>.gitignore</c> rules.</summary>
    public int DirectoriesIgnored { get; init; }

    /// <summary>Symbolic links and junctions that were not followed.</summary>
    public int LinksSkipped { get; init; }

    /// <summary>Directories skipped because they exceed the depth limit.</summary>
    public int DirectoriesTooDeep { get; init; }

    /// <summary>Files or directories that could not be read.</summary>
    public int EntriesUnreadable { get; init; }

    /// <summary>True if the scan stopped at the file-count limit.</summary>
    public bool Truncated { get; init; }

    /// <summary>All files that were seen but not included in the result.</summary>
    public int FilesSkipped => FilesIgnored + EntriesUnreadable;
}
