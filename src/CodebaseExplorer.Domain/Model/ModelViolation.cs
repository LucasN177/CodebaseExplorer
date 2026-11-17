using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Model;

/// <summary>One violated cross-element invariant of an <see cref="ArchitectureModel"/>.</summary>
/// <param name="Code">What kind of invariant is violated.</param>
/// <param name="ElementId">The element that violates it.</param>
/// <param name="Message">Human-readable explanation including the offending reference.</param>
public sealed record ModelViolation(ModelViolationCode Code, ElementId ElementId, string Message)
{
    public override string ToString() => $"{Code} [{ElementId}]: {Message}";
}
