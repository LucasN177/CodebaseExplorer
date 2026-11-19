namespace CodebaseExplorer.AI.Retrieval;

/// <summary>
/// Candidate context of one question, in priority order and not yet budgeted (AI_ARCHITECTURE.md §2 "Retriever"): facts
/// from the model first, then snippets. The <see cref="Prompting.PromptBuilder"/> takes as many as fit.
/// </summary>
internal sealed record RetrievedContext(IReadOnlyList<ContextFact> Facts, IReadOnlyList<ContextSnippet> Snippets);

/// <summary>A statement derived from the analysis model, with the location that proves it, if one exists.</summary>
internal sealed record ContextFact(string Text, string? Path, int? StartLine, int? EndLine, string? ElementId);

/// <summary>Lines of an analyzed file (matching the analyzed version) and why they were chosen.</summary>
internal sealed record ContextSnippet(string Path, int StartLine, int EndLine, string Language, string Text, string Reason, string? ElementId)
{
    public bool Covers(ContextSnippet other) =>
        string.Equals(Path, other.Path, StringComparison.Ordinal) && StartLine <= other.StartLine && EndLine >= other.EndLine;
}
