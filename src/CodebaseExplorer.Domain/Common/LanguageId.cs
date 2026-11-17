namespace CodebaseExplorer.Domain.Common;

/// <summary>
/// Identifies a language or file format (<c>csharp</c>, <c>typescript</c>, <c>python</c>, <c>json</c> …).
/// String-based so new languages need no Domain change (ADR-003). Lowercase, <c>[a-z0-9.+-]+</c>.
/// </summary>
public readonly record struct LanguageId
{
    private LanguageId(string value) => Value = value;

    public static LanguageId CSharp { get; } = new("csharp");
    public static LanguageId TypeScript { get; } = new("typescript");
    public static LanguageId Tsx { get; } = new("tsx");
    public static LanguageId JavaScript { get; } = new("javascript");
    public static LanguageId Python { get; } = new("python");
    public static LanguageId Json { get; } = new("json");
    public static LanguageId Xml { get; } = new("xml");
    public static LanguageId Markdown { get; } = new("markdown");
    public static LanguageId Unknown { get; } = new("unknown");

    /// <summary>The language token; <c>unknown</c> for a default instance.</summary>
    public string Value => field ?? Unknown.Value;

    /// <exception cref="ArgumentException"><paramref name="value"/> is not a valid token.</exception>
    public static LanguageId Create(string value) => new(KeyToken.Validate(value, nameof(value)));

    public override string ToString() => Value;
}
