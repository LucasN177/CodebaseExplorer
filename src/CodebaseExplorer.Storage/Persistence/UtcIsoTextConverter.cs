using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CodebaseExplorer.Storage.Persistence;

/// <summary>
/// Stores <see cref="DateTimeOffset"/> as fixed-width ISO-8601 UTC text, e.g.
/// <c>2026-11-18T09:30:00.0000000Z</c> (DATABASE_SCHEMA.md §1 "Timestamps").
/// </summary>
/// <remarks>
/// EF Core's SQLite provider cannot translate <c>ORDER BY</c> on <see cref="DateTimeOffset"/> columns
/// (verified in STORE-001). With this converter the column is plain TEXT whose lexicographic order equals
/// chronological order, because every value has the same width and offset. The original offset is not
/// kept; reads return the same instant in UTC.
/// </remarks>
internal sealed class UtcIsoTextConverter() : ValueConverter<DateTimeOffset, string>(
    value => value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture),
    text => DateTimeOffset.ParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal))
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
}
