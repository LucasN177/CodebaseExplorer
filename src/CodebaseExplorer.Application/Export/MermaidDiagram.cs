using System.Globalization;
using System.Text;
using CodebaseExplorer.Contracts.Graph;

namespace CodebaseExplorer.Application.Export;

/// <summary>
/// Renders a <see cref="GraphViewDto"/> as a Mermaid flowchart (EXPORT-001). Node ids are generated (<c>n1</c>, <c>n2</c>, …),
/// so names never appear in identifiers; labels are always quoted and escaped with Mermaid entity codes. Expanded nodes
/// become subgraphs. Mermaid's default limit of 500 edges is respected: beyond it the strongest edges are kept.
/// </summary>
public static class MermaidDiagram
{
    /// <summary>Mermaid's default <c>maxEdges</c>; larger diagrams are refused by the default renderer.</summary>
    public const int MaxEdges = 500;

    /// <summary>The diagram (without code fence) and how many edges were left out.</summary>
    public static (string Text, int OmittedEdges) Render(GraphViewDto view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in view.Nodes)
        {
            ids[node.Id] = "n" + (ids.Count + 1).ToString(CultureInfo.InvariantCulture);
        }

        var children = view.Nodes.Where(n => n.ParentId is not null && ids.ContainsKey(n.ParentId))
            .ToLookup(n => n.ParentId!, StringComparer.Ordinal);
        var builder = new StringBuilder("flowchart LR\n");
        foreach (var root in view.Nodes.Where(n => n.ParentId is null || !ids.ContainsKey(n.ParentId)))
        {
            WriteNode(builder, root, children, ids, depth: 1);
        }

        // Edges in node order (the projection's edge order is not stable), so the same model gives the same text.
        var order = view.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        var edges = view.Edges.Where(e => ids.ContainsKey(e.Source) && ids.ContainsKey(e.Target))
            .OrderBy(e => order[e.Source]).ThenBy(e => order[e.Target])
            .ToList();
        var kept = edges.Count <= MaxEdges
            ? edges
            : [.. edges.OrderByDescending(e => e.Count).ThenBy(e => e.Id, StringComparer.Ordinal).Take(MaxEdges)];
        foreach (var edge in kept)
        {
            var arrow = edge.MaxLevel == GraphEvidenceLevels.Heuristic ? "-.->" : "-->";
            var label = edge.Count > 1 ? $"|\"{edge.Count.ToString(CultureInfo.InvariantCulture)}\"|" : string.Empty;
            builder.Append("  ").Append(ids[edge.Source]).Append(' ').Append(arrow).Append(label).Append(' ').Append(ids[edge.Target]).Append('\n');
        }

        return (builder.ToString(), edges.Count - kept.Count);
    }

    /// <summary>
    /// A quoted-label-safe text: every character that could end the label or be read as markup or an entity is written
    /// as a Mermaid entity code (<c>#quot;</c>, <c>#35;</c>, …); line breaks become spaces.
    /// </summary>
    public static string EscapeLabel(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '"' or '#' or '&' or '<' or '>' or '`' or '|' or '[' or ']' or '{' or '}' or '(' or ')' or ';' or '\\' or '*' or '_' or '~':
                    builder.Append('#').Append(((int)c).ToString(CultureInfo.InvariantCulture)).Append(';');
                    break;
                default:
                    builder.Append(char.IsControl(c) ? ' ' : c);
                    break;
            }
        }

        return builder.ToString();
    }

    private static void WriteNode(StringBuilder builder, GraphNodeDto node, ILookup<string, GraphNodeDto> children, Dictionary<string, string> ids, int depth)
    {
        var indent = new string(' ', depth * 2);
        var label = EscapeLabel(node.Badges.Contains(GraphBadges.Missing) ? node.Label + " (missing)" : node.Label);
        var inner = children[node.Id].ToList();
        if (inner.Count == 0)
        {
            builder.Append(indent).Append(ids[node.Id]).Append("[\"").Append(label).Append("\"]\n");
            return;
        }

        builder.Append(indent).Append("subgraph ").Append(ids[node.Id]).Append(" [\"").Append(label).Append("\"]\n");
        foreach (var child in inner)
        {
            WriteNode(builder, child, children, ids, depth + 1);
        }

        builder.Append(indent).Append("end\n");
    }
}
