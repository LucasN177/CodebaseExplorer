using CodebaseExplorer.Analysis.ProjectSystems.DotNet;

namespace CodebaseExplorer.Analysis.Tests.ProjectSystems.DotNet;

public sealed class MsBuildExpressionsTests
{
    private static readonly Dictionary<string, string> Properties = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UseSqlServer"] = "false",
        ["Configuration"] = "Release",
        ["Root"] = "src",
        ["Empty"] = string.Empty,
    };

    private static string? Lookup(string name) => Properties.GetValueOrDefault(name);

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("$(Root)/A.csproj", "src/A.csproj")]
    [InlineData("$(root)-$(CONFIGURATION)", "src-Release")]
    [InlineData("x$(Empty)y", "xy")]
    public void Expand_SubstitutesKnownProperties(string value, string expected) =>
        Assert.Equal(expected, MsBuildExpressions.Expand(value, Lookup, out _));

    [Theory]
    [InlineData("$(Undefined)", "property 'Undefined' is not known statically")]
    [InlineData("$([System.IO.Path]::Combine('a','b'))", "property functions are not evaluated")]
    [InlineData("@(Compile)", "item lists and metadata are not evaluated")]
    [InlineData("%(Identity)", "item lists and metadata are not evaluated")]
    [InlineData("$(Root", "malformed property reference")]
    public void Expand_UnknownOrUnsupported_ReturnsNull(string value, string expectedProblem)
    {
        Assert.Null(MsBuildExpressions.Expand(value, Lookup, out var problem));
        Assert.Equal(expectedProblem, problem);
    }

    [Theory]
    [InlineData(null, nameof(ConditionResult.True))]
    [InlineData("  ", nameof(ConditionResult.True))]
    [InlineData("'$(UseSqlServer)' == 'true'", nameof(ConditionResult.False))]
    [InlineData("'$(UseSqlServer)' != 'true'", nameof(ConditionResult.True))]
    [InlineData("'$(Configuration)' == 'release'", nameof(ConditionResult.True))]
    [InlineData(" '$(Configuration)'=='Release' ", nameof(ConditionResult.True))]
    [InlineData("'Release' == '$(Configuration)'", nameof(ConditionResult.True))]
    [InlineData("'$(Empty)' == ''", nameof(ConditionResult.True))]
    [InlineData("'$(Undefined)' == ''", nameof(ConditionResult.Unknown))]
    [InlineData("'$(A)' == 'x' and '$(B)' == 'y'", nameof(ConditionResult.Unknown))]
    [InlineData("Exists('foo.props')", nameof(ConditionResult.Unknown))]
    [InlineData("$(UseSqlServer)", nameof(ConditionResult.Unknown))]
    [InlineData("'$(TargetFramework.StartsWith(`net`))' == 'True'", nameof(ConditionResult.Unknown))]
    public void EvaluateCondition_SupportsOnlyQuotedEquality(string? condition, string expected) =>
        Assert.Equal(Enum.Parse<ConditionResult>(expected), MsBuildExpressions.EvaluateCondition(condition, Lookup, out _));

    [Fact]
    public void EvaluateCondition_Unknown_ExplainsWhy()
    {
        MsBuildExpressions.EvaluateCondition("Exists('x')", Lookup, out var problem);

        Assert.NotNull(problem);
    }
}
