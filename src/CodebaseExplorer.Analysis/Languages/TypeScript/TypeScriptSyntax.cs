using CodebaseExplorer.Analysis.Languages.TreeSitter;
using CodebaseExplorer.Domain.Entities;
using TreeSitter;

namespace CodebaseExplorer.Analysis.Languages.TypeScript;

/// <summary>A 1-based source span (UTF-16 columns).</summary>
internal readonly record struct Span(int Line, int Column, int EndLine, int EndColumn)
{
    public static Span Of(Node node) => new(
        (int)node.StartPosition.Row + 1, (int)node.StartPosition.Column + 1, (int)node.EndPosition.Row + 1, (int)node.EndPosition.Column + 1);
}

/// <summary>An <c>import</c>, <c>export … from</c>, <c>require(…)</c> or <c>import(…)</c>; specifier <see langword="null"/> if computed.</summary>
internal sealed record ModuleReference(string Syntax, string? Specifier, Span Span);

/// <summary>A top-level declaration that becomes an entity.</summary>
internal sealed record Declaration(string Name, EntityKind Kind, bool IsExported, bool IsDefaultExport, EntityModifiers Modifiers, Span Span);

/// <summary>What one TypeScript/JavaScript file declares and references.</summary>
internal sealed record SourceFileFacts(IReadOnlyList<ModuleReference> References, IReadOnlyList<Declaration> Declarations, bool HasSyntaxErrors);

/// <summary>
/// Extracts module references and top-level declarations from a Tree-sitter TypeScript/TSX/JavaScript tree with
/// cursor walks only (ADR-004 spike result: no queries, no recursion). Comments and string contents are never
/// mistaken for imports because only syntax nodes are inspected.
/// </summary>
internal static class TypeScriptSyntax
{
    public static SourceFileFacts Extract(Tree tree, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var references = new List<ModuleReference>();
        var declarations = new List<Declaration>();
        var exportedNames = new HashSet<string>(StringComparer.Ordinal);

        var root = tree.RootNode;
        foreach (var statement in root.NamedChildren)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (statement.Type)
            {
                case "import_statement":
                    if (StringValue(statement.GetChildForField("source")) is { } imported)
                    {
                        references.Add(new ModuleReference(TypeScriptCodes.SyntaxImport, imported, Span.Of(statement)));
                    }

                    break;
                case "export_statement":
                    ReadExport(statement, references, declarations, exportedNames);
                    break;
                case "expression_statement":
                    ReadCommonJsExport(statement, exportedNames);
                    break;
                default:
                    AddDeclarations(statement, isExported: false, isDefault: false, declarations);
                    break;
            }
        }

        // require(…) and import(…) may appear anywhere (functions, conditionals).
        SyntaxWalk.PreOrder(root, node =>
        {
            if (node.Type == "call_expression" && ReadCall(node) is { } call)
            {
                references.Add(call);
            }

            return true;
        }, cancellationToken);

        var result = declarations
            .Select(d => d.IsExported || !exportedNames.Contains(d.Name) ? d : d with { IsExported = true })
            .ToList();
        references.Sort((a, b) => a.Span.Line != b.Span.Line ? a.Span.Line.CompareTo(b.Span.Line) : a.Span.Column.CompareTo(b.Span.Column));
        return new SourceFileFacts(references, result, root.HasError);
    }

    private static void ReadExport(Node statement, List<ModuleReference> references, List<Declaration> declarations, HashSet<string> exportedNames)
    {
        if (StringValue(statement.GetChildForField("source")) is { } reexported)
        {
            references.Add(new ModuleReference(TypeScriptCodes.SyntaxExport, reexported, Span.Of(statement)));
            return;
        }

        var isDefault = statement.Children.Any(c => c.Type == "default");
        if (statement.GetChildForField("declaration") is { } declaration)
        {
            AddDeclarations(declaration, isExported: true, isDefault, declarations);
            return;
        }

        foreach (var clause in statement.NamedChildren.Where(c => c.Type == "export_clause"))
        {
            foreach (var specifier in clause.NamedChildren.Where(c => c.Type == "export_specifier"))
            {
                if (specifier.GetChildForField("name") is { } name)
                {
                    exportedNames.Add(name.Text);
                }
            }
        }

        if (isDefault && statement.GetChildForField("value") is { } value)
        {
            if (value.Type == "identifier")
            {
                exportedNames.Add(value.Text);
            }
            else
            {
                AddDeclarations(value, isExported: true, isDefault: true, declarations); // export default class {} / function () {}
            }
        }
    }

    /// <summary><c>module.exports = X | { a, b: c }</c>, <c>exports.x = X</c>, <c>module.exports.x = X</c>.</summary>
    private static void ReadCommonJsExport(Node statement, HashSet<string> exportedNames)
    {
        if (statement.NamedChildren.FirstOrDefault() is not { Type: "assignment_expression" } assignment
            || assignment.GetChildForField("left") is not { Type: "member_expression" } left
            || assignment.GetChildForField("right") is not { } right)
        {
            return;
        }

        var target = left.Text.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (target == "module.exports")
        {
            if (right.Type == "identifier")
            {
                exportedNames.Add(right.Text);
            }
            else if (right.Type == "object")
            {
                foreach (var property in right.NamedChildren)
                {
                    if (property.Type == "shorthand_property_identifier")
                    {
                        exportedNames.Add(property.Text);
                    }
                    else if (property.Type == "pair" && property.GetChildForField("value") is { Type: "identifier" } value)
                    {
                        exportedNames.Add(value.Text);
                    }
                }
            }
        }
        else if ((target.StartsWith("exports.", StringComparison.Ordinal) || target.StartsWith("module.exports.", StringComparison.Ordinal))
            && right.Type == "identifier")
        {
            exportedNames.Add(right.Text);
        }
    }

    private static void AddDeclarations(Node node, bool isExported, bool isDefault, List<Declaration> declarations)
    {
        var kind = node.Type switch
        {
            "class_declaration" or "abstract_class_declaration" or "class" => EntityKind.Class,
            "interface_declaration" => EntityKind.Interface,
            "type_alias_declaration" => EntityKind.TypeAlias,
            "enum_declaration" => EntityKind.Enum,
            "function_declaration" or "generator_function_declaration" => EntityKind.Function,
            "function_expression" or "function" or "arrow_function" or "generator_function" when isDefault => EntityKind.Function,
            _ => (EntityKind?)null,
        };

        if (kind is { } entityKind)
        {
            var name = node.GetChildForField("name");
            if (name is null && !isDefault)
            {
                return;
            }

            var modifiers = EntityModifiers.None;
            if (node.Type == "abstract_class_declaration")
            {
                modifiers |= EntityModifiers.Abstract;
            }

            if (node.Children.Any(c => c.Type == "async"))
            {
                modifiers |= EntityModifiers.Async;
            }

            if (node.GetChildForField("type_parameters") is not null)
            {
                modifiers |= EntityModifiers.Generic;
            }

            declarations.Add(new Declaration(name?.Text ?? "default", entityKind, isExported, isDefault, modifiers, Span.Of(name ?? node)));
            return;
        }

        // Variables become entities only when exported (EXPECTED.md §7 of ts-monorepo-workspaces).
        if (isExported && node.Type is "lexical_declaration" or "variable_declaration")
        {
            var isConst = node.Children.FirstOrDefault()?.Type == "const";
            foreach (var declarator in node.NamedChildren.Where(c => c.Type == "variable_declarator"))
            {
                if (declarator.GetChildForField("name") is not { Type: "identifier" } name)
                {
                    continue;
                }

                var value = declarator.GetChildForField("value");
                var isFunction = value?.Type is "arrow_function" or "function_expression" or "function" or "generator_function";
                var modifiers = isFunction && value!.Children.Any(c => c.Type == "async") ? EntityModifiers.Async : EntityModifiers.None;
                declarations.Add(new Declaration(
                    name.Text,
                    isFunction ? EntityKind.Function : isConst ? EntityKind.Constant : EntityKind.Variable,
                    IsExported: true,
                    IsDefaultExport: false,
                    modifiers,
                    Span.Of(name)));
            }
        }
    }

    private static ModuleReference? ReadCall(Node call)
    {
        var function = call.GetChildForField("function");
        var syntax = function?.Type switch
        {
            "import" => TypeScriptCodes.SyntaxDynamicImport,
            "identifier" when function.Text == "require" => TypeScriptCodes.SyntaxRequire,
            _ => null,
        };

        if (syntax is null || call.GetChildForField("arguments") is not { } arguments)
        {
            return null;
        }

        var first = arguments.NamedChildren.FirstOrDefault();
        return first is null ? null : new ModuleReference(syntax, StringValue(first), Span.Of(call));
    }

    /// <summary>The value of a string literal, or of a template literal without substitutions; otherwise <see langword="null"/>.</summary>
    private static string? StringValue(Node? node) => node switch
    {
        { Type: "string" } when node.Text.Length >= 2 => node.Text[1..^1],
        { Type: "template_string" } when node.Text.Length >= 2 && !node.NamedChildren.Any(c => c.Type == "template_substitution") => node.Text[1..^1],
        _ => null,
    };
}
