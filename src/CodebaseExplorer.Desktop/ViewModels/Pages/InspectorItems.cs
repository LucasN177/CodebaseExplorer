using CodebaseExplorer.Contracts.Inspector;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>Relationships of one kind in one direction in the inspector (UI-006), e.g. "uses-type (3)".</summary>
/// <param name="Title">Kind and count.</param>
/// <param name="Items">Related elements.</param>
public sealed record InspectorGroup(string Title, IReadOnlyList<InspectorItem> Items);

/// <summary>A related element or member in the inspector, with what "show in graph" needs.</summary>
/// <param name="Label">Qualified name or path.</param>
/// <param name="Detail">Evidence level badge and occurrences, plus the inner element that holds the relationship.</param>
/// <param name="Level">Evidence level token, or null for members.</param>
/// <param name="Reference">The element and its graph node.</param>
public sealed record InspectorItem(string Label, string? Detail, string? Level, ElementReferenceDto Reference)
{
    public bool CanShowInGraph => Reference.GraphNodeId is not null;

    /// <summary>Accessible name of the item.</summary>
    public override string ToString() => Detail is null ? Label : $"{Label}, {Detail}";

    public static InspectorItem From(RelatedElementDto related)
    {
        var occurrences = related.Occurrences == 1 ? "1 occurrence" : $"{related.Occurrences} occurrences";
        var detail = related.Via is null ? $"{related.Level} · {occurrences}" : $"{related.Level} · {occurrences} · via {related.Via}";
        return new InspectorItem(related.Element.Label, detail, related.Level, related.Element);
    }

    public static InspectorItem Member(ElementReferenceDto member) => new(member.Label, null, null, member);
}

/// <summary>A label/value fact of the inspected element.</summary>
/// <param name="Label">What the value is.</param>
/// <param name="Value">The value.</param>
public sealed record InspectorFact(string Label, string Value);
