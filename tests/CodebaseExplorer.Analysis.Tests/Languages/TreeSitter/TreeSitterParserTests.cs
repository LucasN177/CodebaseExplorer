using System.Diagnostics;
using CodebaseExplorer.Analysis.Languages.TreeSitter;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Tests.Languages.TreeSitter;

/// <summary>
/// Proof code of spike ANALYSIS-008: the bundled grammars load and parse on every CI operating system,
/// positions are UTF-16 based, and hostile input completes within the documented bounds.
/// </summary>
public sealed class TreeSitterParserTests
{
    [Theory]
    [InlineData("TypeScript", "import { a, type B } from './x';\nexport class C<T> { m(): void {} }", "import_statement")]
    [InlineData("Tsx", "import React from 'react';\nexport const C = () => <div>{1}</div>;", "import_statement")]
    [InlineData("JavaScript", "const { x } = require('./a');\nexport class K extends X {}\nconst j = <b/>;", "lexical_declaration")]
    [InlineData("Python", "from ..pkg.mod import (y, z)\nclass K(Base):\n    def m(self): pass", "import_from_statement")]
    public void Every_grammar_parses_valid_source_without_errors(string grammar, string source, string firstStatement)
    {
        using var tree = TreeSitterParser.Parse(Enum.Parse<TreeSitterGrammar>(grammar), source);

        Assert.False(tree.RootNode.HasError);
        Assert.Equal(firstStatement, tree.RootNode.NamedChildren[0].Type);
    }

    [Theory]
    [InlineData("typescript", "TypeScript")]
    [InlineData("tsx", "Tsx")]
    [InlineData("javascript", "JavaScript")]
    [InlineData("python", "Python")]
    public void Languages_map_to_grammars(string language, string expected) =>
        Assert.Equal(Enum.Parse<TreeSitterGrammar>(expected), TreeSitterGrammars.For(LanguageId.Create(language)));

    [Fact]
    public void Other_languages_have_no_grammar() => Assert.Null(TreeSitterGrammars.For(LanguageId.Create("csharp")));

    [Fact]
    public void Positions_are_zero_based_rows_and_utf16_columns()
    {
        using var tree = TreeSitterParser.Parse(TreeSitterGrammar.TypeScript, "const ä = '😀';\n  import x from './y';");

        var import = tree.RootNode.NamedChildren[1];

        Assert.Equal("import_statement", import.Type);
        Assert.Equal(1, import.StartPosition.Row);
        Assert.Equal(2, import.StartPosition.Column);
        Assert.Equal("import x from './y';", import.Text);
    }

    [Fact]
    public void Syntax_errors_produce_a_tree_with_error_nodes_not_an_exception()
    {
        using var tree = TreeSitterParser.Parse(TreeSitterGrammar.Python, "def broken(:\n    import os\nclass");

        Assert.True(tree.RootNode.HasError);
    }

    public static TheoryData<string, string> HostileInputs() => new()
    {
        { "nested-parentheses", new string('(', 1_000_000) },
        { "mixed-brackets", string.Concat(Enumerable.Repeat("{[(<", 250_000)) },
        { "unterminated-template", "`" + string.Concat(Enumerable.Repeat("${`", 150_000)) },
        { "deep-indentation", string.Concat(Enumerable.Range(0, 2_000).Select(depth => new string(' ', depth) + "if x:\n")) },
        { "control-characters", new string(Enumerable.Range(0, 500_000).Select(i => (char)(i % 31 + 1)).ToArray()) },
    };

    [Theory]
    [MemberData(nameof(HostileInputs))]
    public void Hostile_input_is_parsed_and_walked_within_bounds(string name, string source)
    {
        foreach (var grammar in Enum.GetValues<TreeSitterGrammar>())
        {
            var stopwatch = Stopwatch.StartNew();
            using var tree = TreeSitterParser.Parse(grammar, source);
            var nodes = 0L;
            SyntaxWalk.PreOrder(tree.RootNode, _ =>
            {
                nodes++;
                return true;
            }, TestContext.Current.CancellationToken);

            Assert.True(nodes > 0, name);
            // Generous bound for slow CI machines; the spike measured < 3.5 s for 1.9 MB of hostile input.
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"{name}/{grammar} took {stopwatch.Elapsed}");
        }
    }

    [Fact]
    public void Walk_visits_nodes_in_document_order_and_can_skip_subtrees()
    {
        using var tree = TreeSitterParser.Parse(TreeSitterGrammar.TypeScript, "import a from './a';\nfunction f() { require('./b'); }");
        var visited = new List<string>();

        SyntaxWalk.PreOrder(tree.RootNode, node =>
        {
            visited.Add(node.Type);
            return node.Type != "import_statement";
        }, TestContext.Current.CancellationToken);

        Assert.Equal("program", visited[0]);
        Assert.Equal("import_statement", visited[1]);
        Assert.Equal("function_declaration", visited[2]); // the import's children were skipped
        Assert.Contains("call_expression", visited);
    }

    [Fact]
    public void Walk_observes_cancellation()
    {
        using var tree = TreeSitterParser.Parse(TreeSitterGrammar.TypeScript, string.Concat(Enumerable.Repeat("a(b);\n", 10_000)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => SyntaxWalk.PreOrder(tree.RootNode, _ => true, cancellation.Token));
    }

    [Fact]
    public async Task Grammars_can_be_used_from_parallel_threads()
    {
        var source = string.Concat(Enumerable.Range(0, 500).Select(i => $"import {{ a{i} }} from './m{i}';\nexport class C{i} {{}}\n"));

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            using var tree = TreeSitterParser.Parse(TreeSitterGrammar.TypeScript, source);
            return tree.RootNode.NamedChildren.Count;
        }, TestContext.Current.CancellationToken)));

        Assert.All(results, count => Assert.Equal(1_000, count));
    }
}
