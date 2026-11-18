using TreeSitter;

namespace CodebaseExplorer.Analysis.Languages.TreeSitter;

/// <summary>A 1-based source span of a Tree-sitter node (UTF-16 columns, ADR-004 spike result).</summary>
internal readonly record struct Span(int Line, int Column, int EndLine, int EndColumn)
{
    public static Span Of(Node node) => new(
        (int)node.StartPosition.Row + 1, (int)node.StartPosition.Column + 1, (int)node.EndPosition.Row + 1, (int)node.EndPosition.Column + 1);
}
