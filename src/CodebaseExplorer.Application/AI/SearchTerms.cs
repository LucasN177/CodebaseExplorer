using System.Text;

namespace CodebaseExplorer.Application.AI;

/// <summary>
/// Turns text into plain search words (AI-003): identifiers are split at case changes, underscores and digits
/// (<c>InvoiceService</c> → <c>invoice</c>, <c>service</c>; <c>MAX_RETRY</c> → <c>max</c>, <c>retry</c>); words are lowercase
/// ASCII letters/digits only, so nothing produced here can be query syntax.
/// </summary>
public static class SearchTerms
{
    /// <summary>At most this many terms are used for one query.</summary>
    public const int MaxQueryTerms = 24;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "can", "do", "does", "for", "from", "how", "i", "in", "is", "it", "of",
        "on", "or", "the", "this", "to", "what", "when", "where", "which", "who", "why", "with", "you", "me", "my", "we", "our",
        "that", "these", "those", "there", "here", "about", "into", "used", "use", "uses", "using", "get", "set", "new", "var",
        "der", "die", "das", "und", "ist", "wie", "wo", "was", "wird", "werden", "ein", "eine", "einen", "mit", "von", "zu",
        "im", "den", "dem", "des", "auf", "für", "nicht",
    };

    /// <summary>The distinct words of a question, without stop words, in order of first occurrence.</summary>
    public static IReadOnlyList<string> ForQuery(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in Tokens(text))
        {
            // The whole identifier (lowercased) and its parts: "InvoiceService" matches the identifier and the words.
            foreach (var term in Expand(token))
            {
                if (term.Length >= 2 && !StopWords.Contains(term) && seen.Add(term))
                {
                    terms.Add(term);
                    if (terms.Count == MaxQueryTerms)
                    {
                        return terms;
                    }
                }
            }
        }

        return terms;
    }

    /// <summary>
    /// A crude English stem so that <c>orders</c>/<c>order</c> and <c>placed</c>/<c>place</c> meet: one of the suffixes
    /// <c>ing</c>, <c>ies</c> (→ <c>y</c>), <c>es</c>, <c>ed</c>, <c>s</c>, <c>e</c> is removed if at least three
    /// characters remain. Used for matching and as an additional prefix search term, never shown to anyone.
    /// </summary>
    public static string Stem(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        foreach (var (suffix, replacement) in Suffixes)
        {
            if (word.Length - suffix.Length >= 3 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return word[..^suffix.Length] + replacement;
            }
        }

        return word;
    }

    /// <summary>The query terms followed by their stems that differ from every term (for prefix search).</summary>
    public static IReadOnlyList<string> WithStems(IReadOnlyList<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var result = terms.ToList();
        foreach (var stem in terms.Select(Stem))
        {
            if (!result.Contains(stem, StringComparer.Ordinal))
            {
                result.Add(stem);
            }
        }

        return result;
    }

    private static readonly (string Suffix, string Replacement)[] Suffixes =
        [("ing", ""), ("ies", "y"), ("es", ""), ("ed", ""), ("s", ""), ("e", "")];

    /// <summary>
    /// Space-separated words of the identifiers in <paramref name="text"/> that the index would not find otherwise
    /// (the parts of compound identifiers), distinct, at most <paramref name="maxLength"/> characters.
    /// </summary>
    public static string KeywordsOf(string text, int maxLength = 4000)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in Tokens(text))
        {
            var parts = Split(token);
            if (parts.Count < 2)
            {
                continue;
            }

            foreach (var part in parts)
            {
                if (part.Length >= 2 && seen.Add(part))
                {
                    if (builder.Length + part.Length + 1 > maxLength)
                    {
                        return builder.ToString();
                    }

                    builder.Append(builder.Length == 0 ? string.Empty : " ").Append(part);
                }
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<string> Expand(string token)
    {
        yield return token.ToLowerInvariant();
        var parts = Split(token);
        if (parts.Count > 1)
        {
            foreach (var part in parts)
            {
                yield return part;
            }
        }
    }

    /// <summary>Runs of ASCII letters, digits and underscores (identifiers and words).</summary>
    private static IEnumerable<string> Tokens(string text)
    {
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inToken = i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_');
            if (inToken && start < 0)
            {
                start = i;
            }
            else if (!inToken && start >= 0)
            {
                if (i - start <= 64)
                {
                    yield return text[start..i];
                }

                start = -1;
            }
        }
    }

    /// <summary>Lowercase parts of an identifier: split at underscores, lower→upper changes, acronym ends and digits.</summary>
    private static List<string> Split(string token)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            if (c == '_')
            {
                Flush();
                continue;
            }

            if (current.Length > 0)
            {
                var previous = token[i - 1];
                var boundary = (char.IsAsciiLetterLower(previous) && char.IsAsciiLetterUpper(c))
                    || (char.IsAsciiLetterUpper(previous) && char.IsAsciiLetterUpper(c) && i + 1 < token.Length && char.IsAsciiLetterLower(token[i + 1]))
                    || (char.IsAsciiDigit(previous) != char.IsAsciiDigit(c));
                if (boundary)
                {
                    Flush();
                }
            }

            current.Append(char.ToLowerInvariant(c));
        }

        Flush();
        return parts;

        void Flush()
        {
            if (current.Length > 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }
    }
}
