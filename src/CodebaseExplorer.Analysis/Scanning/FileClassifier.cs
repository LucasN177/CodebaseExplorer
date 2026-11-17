using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;

namespace CodebaseExplorer.Analysis.Scanning;

/// <summary>
/// File classification of the scan stage. Precedence: binary, generated, solution file / project
/// manifest, test, then by language (source, documentation, configuration), otherwise other.
/// </summary>
internal static class FileClassifier
{
    private static readonly string[] TestDirectoryNames = ["test", "tests", "__tests__", "spec", "specs"];
    private static readonly string[] TestProjectSuffixes = [".tests", ".test", ".unittests", ".integrationtests", ".specs"];
    private static readonly string[] TestFileInfixes = [".spec.", ".test."];
    private static readonly string[] GeneratedSuffixes = [".g.cs", ".g.i.cs", ".designer.cs", ".generated.cs"];

    public static FileClassification Classify(
        RelativePath path,
        bool isBinary,
        bool hasGeneratedHeader,
        LanguageId language,
        ProjectMarker? marker)
    {
        if (isBinary)
        {
            return FileClassification.Binary;
        }

        if (hasGeneratedHeader || IsGeneratedName(path.FileName))
        {
            return FileClassification.Generated;
        }

        if (marker is { } projectMarker)
        {
            return projectMarker.IsSolution() ? FileClassification.SolutionFile : FileClassification.ProjectManifest;
        }

        if (IsTest(path))
        {
            return FileClassification.Test;
        }

        return language.Value switch
        {
            var l when LanguageDetector.SourceLanguages.Contains(l) => FileClassification.Source,
            var l when LanguageDetector.DocumentationLanguages.Contains(l) => FileClassification.Documentation,
            var l when LanguageDetector.ConfigurationLanguages.Contains(l) => FileClassification.Configuration,
            _ => FileClassification.Other,
        };
    }

    public static bool IsGeneratedName(string fileName) =>
        GeneratedSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    public static bool IsTest(RelativePath path)
    {
        var segments = path.Value.Split('/');
        var directories = segments[..^1];
        var fileName = segments[^1];

        if (directories.Any(d =>
                TestDirectoryNames.Contains(d, StringComparer.OrdinalIgnoreCase)
                || TestProjectSuffixes.Any(s => d.EndsWith(s, StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        if (TestFileInfixes.Any(infix => fileName.Contains(infix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Python conventions: test_*.py and *_test.py
        return fileName.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
            && (fileName.StartsWith("test_", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith("_test.py", StringComparison.OrdinalIgnoreCase));
    }
}
