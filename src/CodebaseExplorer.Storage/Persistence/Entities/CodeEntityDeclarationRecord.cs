namespace CodebaseExplorer.Storage.Persistence.Entities;

/// <summary>Table <c>CodeEntityDeclarations</c> (§4.8): one row per declaration (several for partial types).</summary>
internal sealed class CodeEntityDeclarationRecord
{
    public Guid EntityId { get; set; }

    public int Ordinal { get; set; }

    public Guid FileId { get; set; }

    public int StartLine { get; set; }

    public int StartColumn { get; set; }

    public int EndLine { get; set; }

    public int EndColumn { get; set; }
}
