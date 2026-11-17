using System.Globalization;
using System.Text;

namespace CodebaseExplorer.Domain.Common;

/// <summary>
/// Canonical string that identifies a model element across analysis runs (DOMAIN_MODEL.md §3.2).
/// <see cref="ElementId.FromNaturalKey"/> hashes it into the element's stable id.
/// </summary>
/// <remarks>
/// Kind tokens (ecosystem, module kind, language, relationship kind, analyzer id) must match
/// <c>[a-z0-9.+-]+</c>. Relative paths are normalized (<c>/</c> separators, no leading <c>./</c>,
/// no empty segments, case preserved) and <c>%</c> and <c>:</c> inside them are escaped as
/// <c>%25</c> and <c>%3A</c>, because path components are embedded in other keys (a project key
/// is a prefix of module and entity keys) and must not be confused with the <c>:</c> separator.
/// </remarks>
public readonly record struct NaturalKey
{
    private const string LocationlessEvidenceSuffix = "-:0:0";

    private NaturalKey(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Restores a key from its canonical string (e.g. when reading it back from storage).
    /// No normalization is applied.
    /// </summary>
    public static NaturalKey FromCanonicalString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new NaturalKey(value);
    }

    /// <summary><c>file:{repositoryId}:{relativePath}</c></summary>
    public static NaturalKey ForFile(RepositoryId repositoryId, string relativePath) =>
        new($"file:{repositoryId}:{KeyPath(relativePath, nameof(relativePath))}");

    /// <summary><c>project:{repositoryId}:{ecosystem}:{projectFileOrRootPath}</c></summary>
    public static NaturalKey ForProject(RepositoryId repositoryId, string ecosystemToken, string projectFileOrRootPath) =>
        new($"project:{repositoryId}:{Token(ecosystemToken, nameof(ecosystemToken))}:{KeyPath(projectFileOrRootPath, nameof(projectFileOrRootPath))}");

    /// <summary><c>module:{projectKey}:{moduleKind}:{qualifiedName}</c></summary>
    public static NaturalKey ForModule(NaturalKey projectKey, string moduleKindToken, string qualifiedName) =>
        new($"module:{ProjectKey(projectKey)}:{Token(moduleKindToken, nameof(moduleKindToken))}:{Text(qualifiedName, nameof(qualifiedName))}");

    /// <summary><c>entity:{projectKey}:{language}:{symbolKey}</c></summary>
    public static NaturalKey ForEntity(NaturalKey projectKey, string languageToken, string symbolKey) =>
        new($"entity:{ProjectKey(projectKey)}:{Token(languageToken, nameof(languageToken))}:{Text(symbolKey, nameof(symbolKey))}");

    /// <summary>
    /// <c>extdep:{repositoryId}:{packageEcosystem}:{normalizedName}</c>. The name is normalized per
    /// ecosystem: <c>nuget</c> and <c>dotnet-framework</c> lower-cased, <c>pypi</c> per PEP 503,
    /// all others (e.g. <c>npm</c>) unchanged.
    /// </summary>
    public static NaturalKey ForExternalDependency(RepositoryId repositoryId, string packageEcosystemToken, string packageName)
    {
        var ecosystem = Token(packageEcosystemToken, nameof(packageEcosystemToken));
        var name = NormalizePackageName(ecosystem, Text(packageName, nameof(packageName)));
        return new NaturalKey($"extdep:{repositoryId}:{ecosystem}:{name}");
    }

    /// <summary><c>rel:{sourceId}:{kind}:{targetId}</c></summary>
    public static NaturalKey ForRelationship(ElementId sourceId, string relationshipKindToken, ElementId targetId) =>
        new($"rel:{sourceId}:{Token(relationshipKindToken, nameof(relationshipKindToken))}:{targetId}");

    /// <summary>
    /// <c>evidence:{relationshipId}:{analyzerId}:{rule}:{fileId}:{startLine}:{startColumn}</c>;
    /// pass <see langword="null"/> as <paramref name="fileId"/> for location-less evidence (<c>-:0:0</c>).
    /// </summary>
    public static NaturalKey ForEvidence(
        ElementId relationshipId,
        string analyzerIdToken,
        string rule,
        ElementId? fileId,
        int startLine,
        int startColumn)
    {
        var prefix = $"evidence:{relationshipId}:{Token(analyzerIdToken, nameof(analyzerIdToken))}:{Rule(rule)}";

        if (fileId is null)
        {
            return new NaturalKey($"{prefix}:{LocationlessEvidenceSuffix}");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(startLine, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(startColumn, 1);

        return new NaturalKey(string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}:{fileId.Value}:{startLine}:{startColumn}"));
    }

    /// <summary>
    /// Normalizes a repository-relative path for use in keys: <c>\</c> → <c>/</c>, removes leading
    /// <c>./</c> and empty segments, preserves case. Does not validate (rooted paths and <c>..</c>
    /// segments are rejected by the <c>RelativePath</c> value object).
    /// </summary>
    public static string NormalizePath(string path, string parameterName = "path")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);

        var segments = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(segment => segment == ".")
            .ToArray();

        if (segments.Length == 0)
        {
            throw new ArgumentException($"'{path}' does not contain a path.", parameterName);
        }

        return string.Join('/', segments);
    }

    /// <summary>Normalizes a package name the way the given package ecosystem compares names.</summary>
    public static string NormalizePackageName(string packageEcosystemToken, string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        return packageEcosystemToken switch
        {
            "nuget" or "dotnet-framework" => packageName.Trim().ToLowerInvariant(),
            "pypi" => NormalizePep503(packageName.Trim()),
            _ => packageName.Trim(),
        };
    }

    public override string ToString() => Value;

    private static string NormalizePep503(string name)
    {
        // PEP 503: re.sub(r"[-_.]+", "-", name).lower()
        var builder = new StringBuilder(name.Length);
        var previousWasSeparator = false;
        foreach (var character in name)
        {
            if (character is '-' or '_' or '.')
            {
                if (!previousWasSeparator)
                {
                    builder.Append('-');
                }

                previousWasSeparator = true;
            }
            else
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
        }

        return builder.ToString();
    }

    private static string KeyPath(string path, string parameterName) =>
        NormalizePath(path, parameterName)
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace(":", "%3A", StringComparison.Ordinal);

    private static string ProjectKey(NaturalKey projectKey)
    {
        if (projectKey.Value is null || !projectKey.Value.StartsWith("project:", StringComparison.Ordinal))
        {
            throw new ArgumentException("Expected a project natural key.", nameof(projectKey));
        }

        return projectKey.Value;
    }

    private static string Token(string token, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(token, parameterName);

        foreach (var character in token)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '+' or '-'))
            {
                throw new ArgumentException($"'{token}' is not a valid key token; expected [a-z0-9.+-]+.", parameterName);
            }
        }

        return token;
    }

    private static string Text(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.AsSpan().ContainsAny('\r', '\n'))
        {
            throw new ArgumentException("Key components must not contain line breaks.", parameterName);
        }

        return value;
    }

    private static string Rule(string rule)
    {
        Text(rule, nameof(rule));

        if (rule.AsSpan().ContainsAny(':', ' ', '\t'))
        {
            throw new ArgumentException($"'{rule}' is not a valid rule id; it must not contain ':' or whitespace.", nameof(rule));
        }

        return rule;
    }
}
