namespace CodebaseExplorer.Application.Git;

/// <summary>
/// Remote URL handling shared by the Git adapter and the use cases (GIT_INTEGRATION.md §4, SECURITY.md "Credentials"):
/// URLs are stored, shown and logged only without credentials.
/// </summary>
public static class GitRemoteUrls
{
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
