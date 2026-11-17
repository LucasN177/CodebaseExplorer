namespace CodebaseExplorer.Domain.Entities;

/// <summary>Accessibility of a code element; <c>Exported</c>/<c>NotExported</c> for module-based languages.</summary>
public enum Visibility
{
    Public,
    Internal,
    Protected,
    Private,
    ProtectedInternal,
    PrivateProtected,
    Exported,
    NotExported,
    Unknown,
}
