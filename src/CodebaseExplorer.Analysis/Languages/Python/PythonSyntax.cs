using CodebaseExplorer.Analysis.Languages.TreeSitter;
using CodebaseExplorer.Domain.Entities;
using TreeSitter;

namespace CodebaseExplorer.Analysis.Languages.Python;

/// <summary>
/// One import target of a statement: <c>import a.b</c> → module <c>a.b</c>; <c>from .m import x</c> → module <c>m</c>
/// at level 1 with the imported name <c>x</c> (a submodule if it exists).
/// </summary>
internal sealed record PythonImport(int Level, string Module, string? ImportedName, Span Span);

/// <summary>A top-level class or function.</summary>
internal sealed record PythonDeclaration(string Name, EntityKind Kind, EntityModifiers Modifiers, Span Span);

/// <summary>What one Python file declares and imports.</summary>
internal sealed record PythonFileFacts(IReadOnlyList<PythonImport> Imports, IReadOnlyList<PythonDeclaration> Declarations, bool HasSyntaxErrors);

/// <summary>
/// Extracts imports (anywhere in the file, e.g. inside functions or <c>try</c> blocks) and top-level classes and
/// functions from a Tree-sitter Python tree with cursor walks only (ADR-004 spike result).
/// </summary>
internal static class PythonSyntax
{
    public static PythonFileFacts Extract(Tree tree, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var declarations = new List<PythonDeclaration>();
        foreach (var statement in tree.RootNode.NamedChildren)
        {
            var definition = statement.Type == "decorated_definition" ? statement.GetChildForField("definition") : statement;
            if (definition?.Type is "class_definition" or "function_definition" && definition.GetChildForField("name") is { } name)
            {
                var isClass = definition.Type == "class_definition";
                var modifiers = !isClass && definition.Children.Any(c => c.Type == "async") ? EntityModifiers.Async : EntityModifiers.None;
                if (definition.GetChildForField("type_parameters") is not null)
                {
                    modifiers |= EntityModifiers.Generic;
                }

                declarations.Add(new PythonDeclaration(name.Text, isClass ? EntityKind.Class : EntityKind.Function, modifiers, Span.Of(name)));
            }
        }

        var imports = new List<PythonImport>();
        SyntaxWalk.PreOrder(tree.RootNode, node =>
        {
            switch (node.Type)
            {
                case "import_statement":
                    foreach (var name in node.GetChildrenForField("name"))
                    {
                        if (DottedName(name) is { } module)
                        {
                            imports.Add(new PythonImport(0, module, null, Span.Of(node)));
                        }
                    }

                    return false;
                case "import_from_statement":
                    ReadFromImport(node, imports);
                    return false;
                case "future_import_statement":
                    return false;
                default:
                    return true;
            }
        }, cancellationToken);

        return new PythonFileFacts(imports, declarations, tree.RootNode.HasError);
    }

    private static void ReadFromImport(Node statement, List<PythonImport> imports)
    {
        var source = statement.GetChildForField("module_name");
        int level;
        string module;
        if (source?.Type == "relative_import")
        {
            level = source.Children.FirstOrDefault(c => c.Type == "import_prefix")?.Text.Count(c => c == '.') ?? 0;
            module = source.NamedChildren.FirstOrDefault(c => c.Type == "dotted_name")?.Text ?? string.Empty;
        }
        else if (source?.Type == "dotted_name")
        {
            level = 0;
            module = source.Text;
        }
        else
        {
            return;
        }

        var names = statement.GetChildrenForField("name").Select(DottedName).OfType<string>().ToArray();
        var span = Span.Of(statement);
        if (names.Length == 0)
        {
            imports.Add(new PythonImport(level, module, null, span)); // from x import *
            return;
        }

        foreach (var name in names)
        {
            imports.Add(new PythonImport(level, module, name, span));
        }
    }

    /// <summary>The module path of a <c>dotted_name</c> or the imported name of an <c>aliased_import</c>.</summary>
    private static string? DottedName(Node node) => node.Type switch
    {
        "dotted_name" => string.Concat(node.Text.Where(c => !char.IsWhiteSpace(c))),
        "aliased_import" => node.GetChildForField("name") is { } name ? DottedName(name) : null,
        _ => null,
    };
}
