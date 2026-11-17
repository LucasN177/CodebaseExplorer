namespace CodebaseExplorer.Application.Common;

/// <summary>
/// An expected failure of a use case, returned in a <see cref="Result"/> instead of being thrown
/// (CODING_STANDARDS.md §4).
/// </summary>
/// <remarks>
/// <see cref="Code"/> is stable and machine-readable: lowercase segments separated by dots, using
/// letters, digits and hyphens, e.g. <c>repository.path-not-found</c>. <see cref="Message"/> is for
/// people and may change. <see cref="Details"/> carries structured context (paths, ids); it never
/// contains secrets or source code.
/// </remarks>
public sealed record ApplicationError
{
    public ApplicationError(ErrorKind kind, string code, string message, IReadOnlyDictionary<string, string>? details = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        ValidateCode(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var copiedDetails = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in details ?? new Dictionary<string, string>())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(details));
            copiedDetails[key] = value ?? throw new ArgumentException($"Detail '{key}' has no value.", nameof(details));
        }

        Kind = kind;
        Code = code;
        Message = message.Trim();
        Details = copiedDetails.AsReadOnly();
    }

    public ErrorKind Kind { get; }

    /// <summary>Stable machine-readable code, e.g. <c>repository.path-not-found</c>.</summary>
    public string Code { get; }

    /// <summary>Human-readable explanation.</summary>
    public string Message { get; }

    /// <summary>Structured context, sorted by key.</summary>
    public IReadOnlyDictionary<string, string> Details { get; }

    public static ApplicationError Validation(string code, string message, IReadOnlyDictionary<string, string>? details = null) =>
        new(ErrorKind.Validation, code, message, details);

    public static ApplicationError NotFound(string code, string message, IReadOnlyDictionary<string, string>? details = null) =>
        new(ErrorKind.NotFound, code, message, details);

    public static ApplicationError Conflict(string code, string message, IReadOnlyDictionary<string, string>? details = null) =>
        new(ErrorKind.Conflict, code, message, details);

    public static ApplicationError Failure(string code, string message, IReadOnlyDictionary<string, string>? details = null) =>
        new(ErrorKind.Failure, code, message, details);

    public override string ToString() => $"{Code}: {Message}";

    // Records compare the Details dictionary by reference; compare its content instead.
    public bool Equals(ApplicationError? other) =>
        other is not null
        && Kind == other.Kind
        && Code == other.Code
        && Message == other.Message
        && Details.SequenceEqual(other.Details);

    public override int GetHashCode() => HashCode.Combine(Kind, Code, Message, Details.Count);

    private static void ValidateCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var segments = code.Split('.');
        var valid = segments.All(segment =>
            segment.Length > 0
            && segment.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
            && segment[0] != '-'
            && segment[^1] != '-');

        if (!valid)
        {
            throw new ArgumentException(
                $"'{code}' is not a valid error code; expected lowercase dot-separated segments like 'repository.path-not-found'.",
                nameof(code));
        }
    }
}
