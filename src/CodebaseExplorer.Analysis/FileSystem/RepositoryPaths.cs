using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>Resolves paths written inside repository files (e.g. <c>..\Shop.Domain\Shop.Domain.csproj</c>).</summary>
internal static class RepositoryPaths
{
    /// <summary>
    /// Resolves <paramref name="path"/> relative to <paramref name="baseDirectory"/> (both
    /// repository-relative), handling <c>\</c>, <c>/</c>, <c>.</c> and <c>..</c>. Returns
    /// <see langword="null"/> if the result would leave the repository or the path is rooted.
    /// </summary>
    public static RelativePath? Resolve(RelativePath baseDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var unified = path.Trim().Replace('\\', '/');
        if (unified.StartsWith('/') || (unified.Length >= 2 && char.IsAsciiLetter(unified[0]) && unified[1] == ':'))
        {
            return null;
        }

        var segments = new List<string>(baseDirectory.IsRoot ? [] : baseDirectory.Value.Split('/'));
        foreach (var segment in unified.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? RelativePath.Root : RelativePath.Create(string.Join('/', segments));
    }

    /// <summary>The directory containing <paramref name="file"/> (the root for top-level files).</summary>
    public static RelativePath DirectoryOf(RelativePath file)
    {
        var index = file.Value.LastIndexOf('/');
        return index < 0 ? RelativePath.Root : RelativePath.Create(file.Value[..index]);
    }
}
