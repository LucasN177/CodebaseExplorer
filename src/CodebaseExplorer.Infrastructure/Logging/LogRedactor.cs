using System.Text.RegularExpressions;

namespace CodebaseExplorer.Infrastructure.Logging;

/// <summary>
/// Redaction rules applied to every log line before it is written (CORE-007, SECURITY.md §4): credentials
/// in URLs, authorization headers, secret parameters and well-known token formats are replaced by
/// <see cref="Mask"/>; overly long messages are cut so that no large text (e.g. file content) ends up in a log file.
/// </summary>
public static partial class LogRedactor
{
    /// <summary>Messages (including exception text) longer than this are truncated.</summary>
    public const int MaxLength = 8_000;

    public const string Mask = "***";

    public static string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var redacted = UrlCredentials().Replace(text, "${scheme}" + Mask + "@");
        redacted = AuthorizationHeader().Replace(redacted, "${name}" + Mask);
        redacted = SecretParameter().Replace(redacted, "${name}" + Mask);
        redacted = KnownToken().Replace(redacted, Mask);

        return redacted.Length <= MaxLength
            ? redacted
            : string.Concat(redacted.AsSpan(0, MaxLength), $"… [truncated {redacted.Length - MaxLength} characters]");
    }

    /// <summary><c>https://user:token@host</c> and <c>https://token@host</c>.</summary>
    [GeneratedRegex(@"(?<scheme>\b[a-z][a-z0-9+.\-]*://)[^/\s@]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlCredentials();

    /// <summary><c>Authorization: Bearer abc</c>, <c>authorization=basic abc</c>.</summary>
    [GeneratedRegex(@"(?<name>\b(?:proxy-)?authorization\s*[:=]\s*)(?:(?:bearer|basic|token)\s+)?[^\s,;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeader();

    /// <summary><c>?access_token=…</c>, <c>&amp;api_key=…</c>, <c>password=…</c> and similar.</summary>
    [GeneratedRegex(@"(?<name>\b(?:access_token|refresh_token|token|api[_\-]?key|apikey|client_secret|secret|password|passwd|pwd)\s*[=:]\s*)[^&\s,;""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretParameter();

    /// <summary>GitHub (<c>ghp_…</c>, <c>github_pat_…</c>), GitLab (<c>glpat-…</c>) and <c>sk-…</c> style API keys.</summary>
    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|glpat-[A-Za-z0-9_\-]{20,}|sk-[A-Za-z0-9_\-]{20,})", RegexOptions.CultureInvariant)]
    private static partial Regex KnownToken();
}
