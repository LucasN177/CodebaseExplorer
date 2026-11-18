using TreeSitter;

namespace CodebaseExplorer.Analysis.Languages.TreeSitter;

/// <summary>Iterative (stack-free) traversal of Tree-sitter trees with a tree cursor.</summary>
internal static class SyntaxWalk
{
    /// <summary>
    /// Visits <paramref name="root"/> and its descendants in document order. <paramref name="visit"/> returns
    /// whether to descend into the visited node's children.
    /// </summary>
    public static void PreOrder(Node root, Func<Node, bool> visit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(visit);

        using var cursor = root.Walk();
        var visited = 0;
        while (true)
        {
            if ((++visited & 0xFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (visit(cursor.CurrentNode) && cursor.GotoFirstChild())
            {
                continue;
            }

            while (!cursor.GotoNextSibling())
            {
                if (!cursor.GotoParent())
                {
                    return;
                }
            }
        }
    }
}
