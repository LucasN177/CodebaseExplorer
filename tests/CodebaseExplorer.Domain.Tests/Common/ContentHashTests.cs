using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Domain.Tests.Common;

public sealed class ContentHashTests
{
    private const string Hex = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void Parse_RoundTripsLowercaseHex()
    {
        var hash = ContentHash.Parse(Hex.ToUpperInvariant());

        Assert.Equal(Hex, hash.ToString());
        Assert.Equal(32, hash.Bytes.Length);
    }

    [Fact]
    public void Equality_ComparesBytes()
    {
        var first = ContentHash.Parse(Hex);
        var second = ContentHash.FromBytes(Convert.FromHexString(Hex));

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, ContentHash.FromBytes(new byte[32]));
    }

    [Fact]
    public void FromBytes_CopiesInput()
    {
        var bytes = Convert.FromHexString(Hex);
        var hash = ContentHash.FromBytes(bytes);

        bytes[0] = 0;

        Assert.Equal(Hex, hash.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void FromBytes_WrongLength_Throws(int length) =>
        Assert.Throws<ArgumentException>(() => ContentHash.FromBytes(new byte[length]));

    [Theory]
    [InlineData("abc")]
    [InlineData("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Parse_InvalidHex_Throws(string hex) =>
        Assert.Throws<FormatException>(() => ContentHash.Parse(hex));
}
