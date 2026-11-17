using System.Security.Cryptography;
using System.Text;

namespace CodebaseExplorer.Domain.Common;

/// <summary>
/// Deterministic identity of a model element (file, project, module, entity, relationship,
/// evidence, external dependency): the first 16 bytes of <c>SHA-256(UTF-8(naturalKey))</c>
/// (DOMAIN_MODEL.md §3). The same natural key yields the same id in every process and on every platform.
/// </summary>
/// <remarks>
/// The hash bytes are stored as a <see cref="Guid"/> constructed in big-endian order, so
/// <see cref="ToString"/> renders exactly the hash bytes as 32 lowercase hex characters.
/// </remarks>
public readonly record struct ElementId
{
    private const int HashPrefixLength = 16;

    public ElementId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An element id must not be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    /// <summary>Derives the id of the element identified by <paramref name="naturalKey"/>.</summary>
    public static ElementId FromNaturalKey(NaturalKey naturalKey)
    {
        if (naturalKey.Value is null)
        {
            throw new ArgumentException("The natural key is not initialized.", nameof(naturalKey));
        }

        var keyBytes = Encoding.UTF8.GetBytes(naturalKey.Value);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(keyBytes, hash);

        return new ElementId(new Guid(hash[..HashPrefixLength], bigEndian: true));
    }

    /// <summary>Parses the 32-character lowercase or uppercase hex form produced by <see cref="ToString"/>.</summary>
    public static ElementId Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        if (hex.Length != 32 || !Guid.TryParseExact(hex, "N", out var value))
        {
            throw new FormatException($"'{hex}' is not a 32-character hexadecimal element id.");
        }

        return new ElementId(value);
    }

    /// <summary>32 lowercase hexadecimal characters (the first 16 bytes of the key hash).</summary>
    public override string ToString() => Value.ToString("N");
}
