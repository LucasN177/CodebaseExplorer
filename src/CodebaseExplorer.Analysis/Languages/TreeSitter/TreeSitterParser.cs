using System.Collections.Concurrent;
using TreeSitter;

namespace CodebaseExplorer.Analysis.Languages.TreeSitter;

/// <summary>
/// Thin, thread-safe entry point to the Tree-sitter binding (<c>TreeSitter.DotNet</c>, ADR-004 as amended by
/// spike ANALYSIS-008).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Grammars are loaded once per process and shared; a <see cref="Parser"/> is not thread-safe, so every
/// call creates its own (cheap).</item>
/// <item>The binding offers no parse timeout or cancellation for a single parse. Callers bound the work by the
/// file size limit (<c>AnalysisLimits.MaxParseFileBytes</c>, 2 MB) and check cancellation between files; the spike
/// measured at most ~3.3 s for 1.9 MB of hostile input.</item>
/// <item>Positions: rows are 0-based, columns are 0-based UTF-16 code units (the binding parses UTF-16).</item>
/// <item>Trees can be very wide and, for valid input, deep: walk them only with <see cref="SyntaxWalk"/>
/// (iterative cursor), never recursively; do not use Tree-sitter queries on untrusted input (the spike measured
/// 5 s for an 80 KB file of nested brackets).</item>
/// </list>
/// </remarks>
internal static class TreeSitterParser
{
    private static readonly ConcurrentDictionary<TreeSitterGrammar, Language> Languages = new();

    /// <summary>Parses <paramref name="source"/>; the caller disposes the tree.</summary>
    /// <exception cref="InvalidOperationException">The binding returned no tree.</exception>
    public static Tree Parse(TreeSitterGrammar grammar, string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var language = Languages.GetOrAdd(grammar, static g => new Language(TreeSitterGrammars.BindingName(g)));
        using var parser = new Parser(language);
        return parser.Parse(source)
            ?? throw new InvalidOperationException($"Tree-sitter returned no tree for grammar '{grammar}'.");
    }
}
