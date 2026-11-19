namespace CodebaseExplorer.Application.AI;

/// <summary>
/// A citation found in an answer, checked against the stored model and the context manifest (AI-005,
/// AI_ARCHITECTURE.md §1.3). Unverified citations are shown, but flagged.
/// </summary>
/// <param name="Text">The citation as written, e.g. <c>[F2]</c> or <c>src/A/B.cs:10-20</c>.</param>
/// <param name="Key">The context key (<c>F2</c>) for key citations.</param>
/// <param name="Path">Repository-relative file the citation points to, if known.</param>
/// <param name="StartLine">First cited line.</param>
/// <param name="EndLine">Last cited line.</param>
/// <param name="ElementId">The model element (<c>N</c> format) the citation is about, if any.</param>
/// <param name="Verified">True if the key was sent as context, or the file exists in the model and the lines are within it.</param>
/// <param name="Problem">Why the citation is unverified; null if verified.</param>
public sealed record AiCitation(string Text, string? Key, string? Path, int? StartLine, int? EndLine, string? ElementId, bool Verified, string? Problem);
