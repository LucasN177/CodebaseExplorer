namespace CodebaseExplorer.Contracts.Repositories;

/// <summary>An analysis diagnostic for display.</summary>
/// <param name="Severity"><c>info</c>, <c>warning</c> or <c>error</c>.</param>
/// <param name="Code">Stable diagnostic code.</param>
/// <param name="Message">Human-readable message (never source code).</param>
/// <param name="Path">Repository-relative file path, if the diagnostic has a location in a file that is known.</param>
/// <param name="Line">1-based line, if the diagnostic has a location.</param>
/// <param name="Analyzer">Id of the analyzer that reported it, if any (UI-005).</param>
public sealed record DiagnosticDto(string Severity, string Code, string Message, string? Path = null, int? Line = null, string? Analyzer = null);
