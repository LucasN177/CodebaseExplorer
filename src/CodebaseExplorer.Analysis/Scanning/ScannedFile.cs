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
/// <param name="Language">Detected language; <c>unknown</c> if not recognized.</param>
/// <param name="Marker">Project or solution marker, if the file is one.</param>
/// <param name="Classification">Resulting classification.</param>
internal sealed record ScannedFile(
    RelativePath Path,
    long SizeBytes,
    ContentHash Hash,
    int? LineCount,
    bool IsBinary,
    bool HasGeneratedHeader,
    LanguageId Language,
    ProjectMarker? Marker,
    FileClassification Classification);
