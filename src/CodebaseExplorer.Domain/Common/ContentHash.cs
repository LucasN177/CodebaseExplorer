namespace CodebaseExplorer.Domain.Common;

/// <summary>SHA-256 hash of a file's raw bytes; the basis for change detection (ADR-009).</summary>
public sealed class ContentHash : IEquatable<ContentHash>
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    private ContentHash(byte[] bytes) => _bytes = bytes;

    /// <summary>The 32 hash bytes (copy-free, read-only view).</summary>
    public ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>Creates a hash from exactly 32 bytes (the bytes are copied).</summary>
    public static ContentHash FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException($"A SHA-256 hash has {Length} bytes, got {bytes.Length}.", nameof(bytes));
        }

        return new ContentHash(bytes.ToArray());
    }

    /// <summary>Parses 64 hexadecimal characters.</summary>
    public static ContentHash Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        if (hex.Length != Length * 2)
        {
            throw new FormatException($"A SHA-256 hash has {Length * 2} hex characters, got {hex.Length}.");
        }

        return new ContentHash(Convert.FromHexString(hex));
    }

    public bool Equals(ContentHash? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => Equals(obj as ContentHash);

    public override int GetHashCode() => BitConverter.ToInt32(_bytes, 0);

    /// <summary>64 lowercase hexadecimal characters.</summary>
    public override string ToString() => Convert.ToHexStringLower(_bytes);

    public static bool operator ==(ContentHash? left, ContentHash? right) => Equals(left, right);

    public static bool operator !=(ContentHash? left, ContentHash? right) => !Equals(left, right);
}
