namespace CodebaseExplorer.Application.AI;

/// <summary>
/// What was sent to the model as context (AI_ARCHITECTURE.md §1.6 "transparent", §4): every fact and snippet with its
/// citation key. Stored with each answer (<c>AIMessages.ContextManifestJson</c>) and shown to the user.
/// </summary>
/// <param name="Entries">The context entries in the order they were sent.</param>
/// <param name="BudgetTokens">Tokens available for context after system prompt, history, question and answer reserve.</param>
/// <param name="UsedTokens">Estimated tokens of the entries sent.</param>
/// <param name="OmittedEntries">Candidates that did not fit into the budget.</param>
public sealed record AiContextManifest(IReadOnlyList<AiContextEntry> Entries, int BudgetTokens, int UsedTokens, int OmittedEntries)
{
    public static AiContextManifest Empty { get; } = new([], 0, 0, 0);

    /// <summary>The entry with the citation key (e.g. <c>F1</c>), if any.</summary>
    public AiContextEntry? Find(string key) =>
        Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One fact or snippet of the context.</summary>
/// <param name="Key">Citation key: <c>F</c>n for facts, <c>S</c>n for snippets.</param>
/// <param name="Kind"><see cref="FactKind"/> or <see cref="SnippetKind"/>.</param>
/// <param name="Label">The fact text, or the reason a snippet was chosen.</param>
/// <param name="Path">Repository-relative file of the evidence or snippet.</param>
/// <param name="StartLine">First line (1-based) of the evidence or snippet.</param>
/// <param name="EndLine">Last line (inclusive).</param>
/// <param name="ElementId">The model element the entry is about (<c>N</c> format), if any.</param>
/// <param name="Tokens">Estimated tokens of the entry as sent.</param>
public sealed record AiContextEntry(string Key, string Kind, string Label, string? Path, int? StartLine, int? EndLine, string? ElementId, int Tokens)
{
    public const string FactKind = "fact";

    public const string SnippetKind = "snippet";
}

/// <summary>Role of a chat message.</summary>
public enum AiRole
{
    System,
    User,
    Assistant,
}

/// <summary>A message as sent to the model (text only, no tools).</summary>
public sealed record AiPromptMessage(AiRole Role, string Text);
