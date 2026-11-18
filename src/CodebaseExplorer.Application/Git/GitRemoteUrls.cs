using System.Buffers;
using CodebaseExplorer.Application.Common;
using CodebaseExplorer.Domain.Repositories;

namespace CodebaseExplorer.Application.Git;

/// <summary>
/// Remote URL handling shared by the Git adapter and the use cases (GIT_INTEGRATION.md §4, SECURITY.md "Credentials"):
/// URLs are stored, shown and logged only without credentials.
/// </summary>
public static class GitRemoteUrls
{
    private static readonly SearchValues<char> SegmentCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.");

    /// <summary>
    /// Validates a remote entered by the user for cloning (GIT-003). Accepted: <c>https://host[:port]/owner/…/repo[.git]</c>
    /// (exactly <c>owner/repo</c> on github.com). Rejected: every other scheme (<c>http</c>, <c>ssh</c>, <c>git</c>,
    /// <c>file</c>, …), scp-like addresses and local paths — libgit2 would start the system <c>ssh</c> for SSH remotes
    /// (GIT-006) —, query strings, fragments, and path segments other than letters, digits, <c>-</c>, <c>_</c> and
    /// <c>.</c> (no <c>.</c>/<c>..</c>, no trailing dot). Credentials in the URL are removed and reported.
    /// </summary>
    public static Result<GitCloneUrl> ParseCloneUrl(string input)
    {
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return Invalid(GitErrorCodes.UrlInvalid, "Enter the HTTPS address of a Git repository.");
        }

        var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0 || !text[..schemeEnd].Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(GitErrorCodes.UrlSchemeNotSupported,
                "Only HTTPS remotes (https://…) can be cloned; SSH, git://, file:// and local paths are not supported.");
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            return Invalid(GitErrorCodes.UrlInvalid, "The address is not a valid URL.");
        }

        if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return Invalid(GitErrorCodes.UrlInvalid, "The address must not contain a query (?…) or fragment (#…).");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length > 0 && segments[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            segments[^1] = segments[^1][..^4];
        }

        if (segments.Length < 2 || !segments.All(IsSafeSegment))
        {
            return Invalid(GitErrorCodes.UrlInvalid,
                "The address must name an owner and a repository, e.g. https://github.com/owner/repository.");
        }

        var host = uri.IdnHost.ToLowerInvariant();
        var kind = host switch
        {
            "github.com" or "www.github.com" => RepositorySourceKind.GitHub,
            "gitlab.com" or "www.gitlab.com" => RepositorySourceKind.GitLab,
            _ => RepositorySourceKind.OtherGitRemote,
        };

        if (kind == RepositorySourceKind.GitHub && segments.Length != 2)
        {
            return Invalid(GitErrorCodes.UrlInvalid, "A GitHub address has the form https://github.com/owner/repository.");
        }

        var url = new Uri(uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.UriEscaped)
            .TrimEnd('/'));
        var hostSegment = uri.IsDefaultPort ? host : $"{host}_{uri.Port}";
        string[] cloneSegments = [hostSegment, .. segments.Select(s => s.ToLowerInvariant())];
        return new GitCloneUrl(url, kind, cloneSegments, CredentialsRemoved: uri.UserInfo.Length > 0);
    }

    private static bool IsSafeSegment(string segment) =>
        segment.Length is > 0 and <= 100
        && !segment.AsSpan().ContainsAnyExcept(SegmentCharacters)
        && !segment.EndsWith('.'); // also rejects "." and ".."; a trailing dot is dropped by Windows file names

    private static ApplicationError Invalid(string code, string message) => ApplicationError.Validation(code, message);

    /// <summary>
    /// Removes everything that can carry a secret: user information (<c>user:token@</c>), query and fragment of URLs
    /// with a scheme, and the user part of scp-like addresses (<c>user@host:path</c>). Local paths are returned
    /// unchanged; unparseable URLs with a scheme are replaced by <c>&lt;scheme&gt;://&lt;invalid&gt;</c>.
    /// </summary>
    public static string Sanitize(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var trimmed = url.Trim();

        var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0)
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return $"{trimmed[..schemeEnd].ToLowerInvariant()}://<invalid>";
            }

            if (uri.IsFile)
            {
                return uri.AbsoluteUri;
            }

            // Port only when it is not the scheme's default; no user information, query or fragment.
            return uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.UriEscaped);
        }

        return IsScpLike(trimmed, out var at) ? trimmed[(at + 1)..] : trimmed;
    }

    /// <summary>
    /// <c>[user@]host:path</c> without a scheme, as accepted by Git. A colon after a single drive letter (<c>C:\…</c>)
    /// or after a slash means a local path.
    /// </summary>
    private static bool IsScpLike(string url, out int userSeparator)
    {
        userSeparator = -1;
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        var slash = url.IndexOfAny(['/', '\\']);
        if (colon <= 1 || (slash >= 0 && slash < colon))
        {
            return false;
        }

        userSeparator = url.LastIndexOf('@', colon);
        return true;
    }
}
