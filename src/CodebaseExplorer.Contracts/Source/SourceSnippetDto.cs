namespace CodebaseExplorer.Contracts.Source;

/// <summary>
/// A read-only excerpt of a repository file around an evidence location (UI-007). Plain text only; it is shown in
/// the UI and never logged or sent anywhere.
/// </summary>
public sealed record SourceSnippetDto
{
    /// <summary>Repository-relative path.</summary>
    public required string Path { get; init; }

    /// <summary>Absolute path of the file on this machine (for "copy path").</summary>
    public required string AbsolutePath { get; init; }

    /// <summary>The lines of the excerpt in order.</summary>
    public IReadOnlyList<SourceLineDto> Lines { get; init; } = [];

    /// <summary>True if the file's content differs from the analyzed version: lines may have moved.</summary>
    public bool ChangedSinceAnalysis { get; init; }

    /// <summary>True if the file now has fewer lines than the evidence range; only the existing ones are shown.</summary>
    public bool RangeTruncated { get; init; }
}

/// <param name="Number">1-based line number.</param>
/// <param name="Text">Line content without line break; very long lines are shortened.</param>
/// <param name="IsEvidence">True for the lines of the evidence range.</param>
public sealed record SourceLineDto(int Number, string Text, bool IsEvidence);
