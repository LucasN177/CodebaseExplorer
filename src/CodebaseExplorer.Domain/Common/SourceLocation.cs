namespace CodebaseExplorer.Domain.Common;

/// <summary>
/// A 1-based line/column range inside a repository file. The start position is never after the
/// end position.
/// </summary>
public readonly record struct SourceLocation
{
    public SourceLocation(ElementId fileId, int startLine, int startColumn, int endLine, int endColumn)
    {
        if (fileId.Value == Guid.Empty)
        {
            throw new ArgumentException("A source location requires a file id.", nameof(fileId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(startLine, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(startColumn, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(endLine, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(endColumn, 1);

        if (endLine < startLine || (endLine == startLine && endColumn < startColumn))
        {
            throw new ArgumentException(
                $"The range {startLine}:{startColumn}-{endLine}:{endColumn} ends before it starts.");
        }

        FileId = fileId;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public ElementId FileId { get; }

    public int StartLine { get; }

    public int StartColumn { get; }

    public int EndLine { get; }

    public int EndColumn { get; }

    /// <summary>Creates a location covering a single line.</summary>
    public static SourceLocation Line(ElementId fileId, int line, int startColumn = 1, int endColumn = 1) =>
        new(fileId, line, startColumn, line, Math.Max(startColumn, endColumn));
}
