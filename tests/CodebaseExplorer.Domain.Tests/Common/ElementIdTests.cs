using System.Security.Cryptography;
using System.Text;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Tests.Common;

public sealed class ElementIdTests
{
    [Fact]
    public void FromNaturalKey_KnownVector_IsFirst16BytesOfSha256()
    {
        // SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223 b00361a396177a9cb410ff61f20015ad (FIPS 180-2 test vector)
        var id = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("abc"));

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223", id.ToString());
    }

    [Theory]
    [InlineData("file:0193a1b2c3d4e5f60718293a4b5c6d7e:src/Billing/Invoice.cs")]
    [InlineData("entity:project:x:dotnet:a.csproj:csharp:T:Billing.Invoices.InvoiceService")]
    [InlineData("module:project:x:dotnet:Ä/Ö.csproj:namespace:Grüße")]
    public void FromNaturalKey_MatchesUtf8Sha256Prefix(string key)
    {
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];

        var id = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString(key));

        Assert.Equal(expected, id.ToString());
    }

    [Fact]
    public void FromNaturalKey_SameKey_ReturnsEqualIds()
    {
        var first = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("project:a:dotnet:src/A.csproj"));
        var second = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("project:a:dotnet:src/A.csproj"));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void FromNaturalKey_DifferentKeys_ReturnDifferentIds()
    {
        var first = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("project:a:dotnet:src/A.csproj"));
        var second = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("project:a:dotnet:src/a.csproj"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ToString_Is32LowercaseHexCharacters()
    {
        var text = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("anything")).ToString();

        Assert.Matches("^[0-9a-f]{32}$", text);
    }

    [Fact]
    public void Parse_RoundTripsToString()
    {
        var id = ElementId.FromNaturalKey(NaturalKey.FromCanonicalString("round-trip"));

        Assert.Equal(id, ElementId.Parse(id.ToString()));
        Assert.Equal(id, ElementId.Parse(id.ToString().ToUpperInvariant()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("ba7816bf-8f01-cfea-4141-40de5dae2223")]
    [InlineData("ba7816bf8f01cfea414140de5dae222")]
    public void Parse_InvalidText_Throws(string text) =>
        Assert.Throws<FormatException>(() => ElementId.Parse(text));

    [Fact]
    public void Constructor_EmptyGuid_Throws() =>
        Assert.Throws<ArgumentException>(() => new ElementId(Guid.Empty));

    [Fact]
    public void FromNaturalKey_DefaultKey_Throws() =>
        Assert.Throws<ArgumentException>(() => ElementId.FromNaturalKey(default));
}
