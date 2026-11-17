using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;

namespace CodebaseExplorer.Analysis.Scanning;

/// <summary>A file found by the scanner (ANALYSIS_ENGINE.md Stage 1).</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="SizeBytes">Size in bytes.</param>
/// <param name="Hash">SHA-256 of the raw bytes.</param>
/// <param name="LineCount">Number of lines for text files; <see langword="null"/> for binary files.</param>
/// <param name="IsBinary">True if a NUL byte occurs in the first 8 KB.</param>
/// <param name="HasGeneratedHeader">True if an <c>&lt;auto-generated</c> marker occurs in the first 8 KB.</param>
/// <param name="Classification">Location- and name-based classification.</param>
internal sealed record ScannedFile(
    RelativePath Path,
    long SizeBytes,
    ContentHash Hash,
    int? LineCount,
    bool IsBinary,
    bool HasGeneratedHeader,
    FileClassification Classification);
