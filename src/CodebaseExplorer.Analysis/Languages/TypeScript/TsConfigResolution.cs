using CodebaseExplorer.Analysis.FileSystem;
using CodebaseExplorer.Analysis.ProjectSystems.Node;
using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.Languages.TypeScript;

/// <summary>
/// The module-resolution settings of a project's <c>tsconfig.json</c> after following its relative <c>extends</c>
/// chain (at most <see cref="MaxExtendsDepth"/> files, inside the repository): <c>baseUrl</c> and <c>paths</c> as in
/// TypeScript — <c>paths</c> come from the nearest config that declares them and are relative to the effective
/// <c>baseUrl</c>, or to the declaring config's folder if there is none.
/// </summary>
internal sealed record TsConfigResolution(RelativePath? BaseUrl, RelativePath? PathsBase, IReadOnlyDictionary<string, IReadOnlyList<string>> Paths)
{
    public const int MaxExtendsDepth = 5;

    public static TsConfigResolution None { get; } = new(null, null, new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>Loads the chain; <paramref name="load"/> returns the content or an error message.</summary>
    public static async Task<(TsConfigResolution Resolution, IReadOnlyList<string> Problems)> LoadAsync(
        RelativePath tsconfig, Func<RelativePath, Task<(string? Content, string? Error)>> load)
    {
        ArgumentNullException.ThrowIfNull(load);

        var problems = new List<string>();
        var chain = new List<(RelativePath Path, TsConfigFile Config)>();
        RelativePath? current = tsconfig;
        while (current is { } path && chain.Count < MaxExtendsDepth && chain.All(c => c.Path != path))
        {
            var (content, error) = await load(path);
            if (content is null)
            {
                problems.Add($"{path}: {error}");
                break;
            }

            if (!TsConfigFile.TryParse(content, out var config, out var parseError))
            {
                problems.Add($"{path}: {parseError}");
                break;
            }

            chain.Add((path, config!));
            current = config!.Extends is { } extends ? ResolveExtends(RepositoryPaths.DirectoryOf(path), extends) : null;
            if (config.Extends is { } written && current is null)
            {
                problems.Add($"{path}: 'extends' target '{written}' lies outside the repository.");
            }
        }

        RelativePath? baseUrl = null;
        foreach (var (path, config) in chain)
        {
            if (config.BaseUrl is { } value)
            {
                baseUrl = RepositoryPaths.Resolve(RepositoryPaths.DirectoryOf(path), value);
                break;
            }
        }

        foreach (var (path, config) in chain)
        {
            if (config.Paths is { } paths)
            {
                return (new TsConfigResolution(baseUrl, baseUrl ?? RepositoryPaths.DirectoryOf(path), paths), problems);
            }
        }

        return (new TsConfigResolution(baseUrl, null, new Dictionary<string, IReadOnlyList<string>>()), problems);
    }

    /// <summary>
    /// The substitutions of the best matching <c>paths</c> pattern (exact match, else the wildcard pattern with the
    /// longest prefix), already resolved to repository paths; empty if no pattern matches.
    /// </summary>
    public IReadOnlyList<RelativePath> Substitute(string specifier)
    {
        if (PathsBase is not { } pathsBase || Paths.Count == 0)
        {
            return [];
        }

        string? captured = null;
        IReadOnlyList<string>? targets = null;
        if (Paths.TryGetValue(specifier, out var exact))
        {
            targets = exact;
            captured = string.Empty;
        }
        else
        {
            var bestPrefix = -1;
            foreach (var (pattern, substitutions) in Paths)
            {
                var star = pattern.IndexOf('*', StringComparison.Ordinal);
                if (star < 0 || pattern.IndexOf('*', star + 1) >= 0)
                {
                    continue;
                }

                var prefix = pattern[..star];
                var suffix = pattern[(star + 1)..];
                if (specifier.Length >= prefix.Length + suffix.Length
                    && specifier.StartsWith(prefix, StringComparison.Ordinal)
                    && specifier.EndsWith(suffix, StringComparison.Ordinal)
                    && prefix.Length > bestPrefix)
                {
                    bestPrefix = prefix.Length;
                    targets = substitutions;
                    captured = specifier[prefix.Length..(specifier.Length - suffix.Length)];
                }
            }
        }

        return targets is null
            ? []
            : [.. targets.Select(t => RepositoryPaths.Resolve(pathsBase, t.Replace("*", captured, StringComparison.Ordinal))).OfType<RelativePath>()];
    }

    private static RelativePath? ResolveExtends(RelativePath directory, string extends)
    {
        var path = RepositoryPaths.Resolve(directory, extends);
        if (path is { } resolved && !resolved.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            path = RelativePath.Create(resolved.Value + ".json");
        }

        return path;
    }
}
