using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Tests.Common;

public sealed class LanguageIdTests
{
    [Theory]
    [InlineData("csharp")]
    [InlineData("c++")]
    [InlineData("objective-c")]
    [InlineData("f.sharp")]
    public void Create_AcceptsTokens(string token) => Assert.Equal(token, LanguageId.Create(token).Value);

    [Theory]
    [InlineData("CSharp")]
    [InlineData("c sharp")]
    [InlineData("")]
    public void Create_RejectsInvalidTokens(string token) =>
        Assert.ThrowsAny<ArgumentException>(() => LanguageId.Create(token));

    [Fact]
    public void WellKnownLanguages_EqualCreatedOnes() =>
        Assert.Equal(LanguageId.CSharp, LanguageId.Create("csharp"));

    [Fact]
    public void Default_IsUnknown() => Assert.Equal("unknown", default(LanguageId).Value);
}
