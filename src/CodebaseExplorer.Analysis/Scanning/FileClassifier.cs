using CodebaseExplorer.Domain.Common;
using CodebaseExplorer.Domain.Files;

namespace CodebaseExplorer.Analysis.Scanning;

/// <summary>
/// Name- and location-based file classification of the scan stage. Language-dependent classes
/// (source, documentation, configuration, project manifest, solution file) are assigned by
/// language and marker detection (REPO-003).
/// </summary>
internal static class FileClassifier
{
    private static readonly string[] TestDirectoryNames = ["test", "tests", "__tests__", "spec", "specs"];
    private static readonly string[] TestProjectSuffixes = [".tests", ".test", ".unittests", ".integrationtests", ".specs"];
    private static readonly string[] TestFileInfixes = [".spec.", ".test."];
    private static readonly string[] GeneratedSuffixes = [".g.cs", ".g.i.cs", ".designer.cs", ".generated.cs"];

    /// <summary>Order: binary, generated, test, other.</summary>
    public static FileClassification Classify(RelativePath path, bool isBinary, bool hasGeneratedHeader)
    {
        if (isBinary)
        {
            return FileClassification.Binary;
        }

        if (hasGeneratedHeader || IsGeneratedName(path.FileName))
        {
            return FileClassification.Generated;
        }

        return IsTest(path) ? FileClassification.Test : FileClassification.Other;
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
