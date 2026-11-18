namespace CodebaseExplorer.Contracts.Inspector;

/// <summary>
/// Details of one model element for the Component Inspector (UI-006, USER_WORKFLOWS.md WF-04): facts, metrics,
/// members and the relationships crossing the element's boundary, grouped by kind. Names, paths and metrics only —
/// never source code.
/// </summary>
public sealed record ElementDetailsDto
{
    /// <summary>Element id (32 hex characters).</summary>
    public required string Id { get; init; }

    /// <summary><c>project</c>, <c>module</c>, <c>entity</c> or <c>external-dependency</c>.</summary>
    public required string Kind { get; init; }

    public required string Name { get; init; }

    /// <summary>Readable kind, e.g. <c>Project</c>, <c>Namespace</c>, <c>Class</c>, <c>External dependency</c>.</summary>
    public required string KindText { get; init; }

    /// <summary>Label/value facts, e.g. path, ecosystem, language, target frameworks, visibility, declarations.</summary>
    public IReadOnlyList<ElementFactDto> Facts { get; init; } = [];

    /// <summary>Distinct elements with a relationship into this element (its subtree for projects, modules and types).</summary>
    public int FanIn { get; init; }

    /// <summary>Distinct elements this element (its subtree) has a relationship to.</summary>
    public int FanOut { get; init; }

    /// <summary>Size measure: files (project), entities (module), lines (entity), 0 otherwise.</summary>
    public int Size { get; init; }

    /// <summary>Members of a type (methods, properties, …), or the types of a module; empty otherwise.</summary>
    public IReadOnlyList<ElementReferenceDto> Members { get; init; } = [];

    public IReadOnlyList<RelationshipGroupDto> Incoming { get; init; } = [];

    public IReadOnlyList<RelationshipGroupDto> Outgoing { get; init; } = [];
}

/// <param name="Label">What the value is, e.g. <c>Path</c>.</param>
/// <param name="Value">The value.</param>
public sealed record ElementFactDto(string Label, string Value);

/// <summary>Relationships of one kind (e.g. <c>uses-type</c>) in one direction.</summary>
/// <param name="Kind">Relationship kind token (Domain key token, e.g. <c>project-reference</c>).</param>
/// <param name="Items">The related elements, strongest evidence first.</param>
public sealed record RelationshipGroupDto(string Kind, IReadOnlyList<RelatedElementDto> Items);

/// <summary>The element on the other side of a relationship.</summary>
/// <param name="Element">The related element.</param>
/// <param name="Level">Evidence level: <c>confirmed</c>, <c>syntactic</c> or <c>heuristic</c>.</param>
/// <param name="Occurrences">Number of evidence records.</param>
/// <param name="Via">Inner element of the inspected element that holds the relationship, if it is not the element itself (e.g. a method of the type).</param>
public sealed record RelatedElementDto(ElementReferenceDto Element, string Level, int Occurrences, string? Via = null);

/// <summary>
/// A model element with what "show in graph" needs: the node to focus and the nodes to expand so that it is visible.
/// </summary>
/// <param name="Id">Element id.</param>
/// <param name="Kind">Graph node kind token (<c>project</c>, <c>module</c>, <c>entity</c>, <c>external-dependency</c>) or <c>file</c>.</param>
/// <param name="Label">Readable (qualified) name or path.</param>
/// <param name="GraphNodeId">The graph node that represents the element (a member is shown by its outermost type; a file by its project); null if it has no node.</param>
/// <param name="ExpandPath">Ancestors to expand, outermost first (project, namespaces), so that <paramref name="GraphNodeId"/> is visible.</param>
public sealed record ElementReferenceDto(string Id, string Kind, string Label, string? GraphNodeId, IReadOnlyList<string> ExpandPath);
