namespace CodebaseExplorer.Domain.Common;

/// <summary>Typed reference to a model element; the endpoint of a relationship.</summary>
public readonly record struct ElementReference
{
    public ElementReference(ElementKind kind, ElementId id)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An element reference requires an id.", nameof(id));
        }

        Kind = kind;
        Id = id;
    }

    public ElementKind Kind { get; }

    public ElementId Id { get; }

    public override string ToString() => $"{Kind}:{Id}";
}
