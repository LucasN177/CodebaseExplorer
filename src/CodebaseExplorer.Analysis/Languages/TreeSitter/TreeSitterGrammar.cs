using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Languages.TreeSitter;

/// <summary>
/// The Tree-sitter grammars Codebase Explorer uses (ADR-004, spike ANALYSIS-008). Only these grammars are
/// shipped; all other native grammar libraries of the binding package are removed from the build output
/// (<c>Directory.Build.targets</c>).
/// </summary>
internal enum TreeSitterGrammar
{
    TypeScript,
    Tsx,
    JavaScript,
    Python,
}

/// <summary>Maps languages and grammars to the names the binding loads.</summary>
internal static class TreeSitterGrammars
{
    /// <summary>The grammar that parses files of <paramref name="language"/>, if any.</summary>
    public static TreeSitterGrammar? For(LanguageId language) => language.Value switch
    {
        "typescript" => TreeSitterGrammar.TypeScript,
        "tsx" => TreeSitterGrammar.Tsx,
        "javascript" => TreeSitterGrammar.JavaScript, // the JavaScript grammar covers JSX
        "python" => TreeSitterGrammar.Python,
        _ => null,
    };

    /// <summary>Language name understood by <c>TreeSitter.Language(string)</c>.</summary>
    public static string BindingName(TreeSitterGrammar grammar) => grammar switch
    {
        TreeSitterGrammar.TypeScript => "TypeScript",
        TreeSitterGrammar.Tsx => "TSX",
        TreeSitterGrammar.JavaScript => "JavaScript",
        TreeSitterGrammar.Python => "Python",
        _ => throw new ArgumentOutOfRangeException(nameof(grammar), grammar, "Unknown grammar."),
    };
}
