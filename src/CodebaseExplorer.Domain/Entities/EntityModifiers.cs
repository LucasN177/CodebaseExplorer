namespace CodebaseExplorer.Domain.Entities;

/// <summary>Modifiers of a code element (combinable).</summary>
[Flags]
public enum EntityModifiers
{
    None = 0,
    Abstract = 1 << 0,
    Static = 1 << 1,
    Sealed = 1 << 2,
    Async = 1 << 3,
    Partial = 1 << 4,
    Generic = 1 << 5,
    Generated = 1 << 6,
}
