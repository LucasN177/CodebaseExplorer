namespace CodebaseExplorer.Domain.Common;

/// <summary>
/// Identifies an analyzer, e.g. <c>dotnet.msbuild-static</c>, <c>csharp.roslyn</c>,
/// <c>treesitter.typescript</c>. Lowercase, <c>[a-z0-9.+-]+</c>.
/// </summary>
public readonly record struct AnalyzerId
{
    private AnalyzerId(string value) => Value = value;

    public string Value { get; }

    /// <exception cref="ArgumentException"><paramref name="value"/> is not a valid token.</exception>
    public static AnalyzerId Create(string value) => new(KeyToken.Validate(value, nameof(value)));

    public override string ToString() => Value;
}
