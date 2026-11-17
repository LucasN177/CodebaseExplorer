namespace CodebaseExplorer.Domain.Entities;

/// <summary>Kind of a named code element.</summary>
public enum EntityKind
{
    Class,
    Interface,
    Struct,
    Record,
    Enum,
    Delegate,
    TypeAlias,
    Function,
    Method,
    Constructor,
    Property,
    Field,
    Event,
    Constant,
    Variable,
}
