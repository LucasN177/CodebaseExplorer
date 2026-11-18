using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using CodebaseExplorer.TestRepositories;

namespace CodebaseExplorer.IntegrationTests.Security;

/// <summary>
/// Reusable harness for hostile-fixture tests (QA-003; also for npm, Python and Git hostile tests in
/// ANALYSIS-009, ANALYSIS-011, GIT-001). Documented in TEST_STRATEGY.md §5.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Marker check:</b> hostile payloads write <c>MARKER-*</c> files when executed. The fixture is copied to
/// a temporary folder, its files are snapshotted (path + SHA-256), and after the analysis the folder — plus
/// any other directory a payload could write to — must be unchanged and free of markers.</item>
/// <item><b>Process-spawn guard:</b> .NET has no cross-platform hook for observing child processes, so the guard is
/// static: the IL of the modules that touch repository content must not reference
/// <c>System.Diagnostics.Process</c>/<c>ProcessStartInfo</c> or P/Invoke process-creation functions.</item>
/// </list>
/// </remarks>
internal static class ZeroExecutionHarness
{
    public const string MarkerPrefix = "MARKER-";

    private static readonly HashSet<string> ProcessTypes = new(StringComparer.Ordinal)
    {
        "System.Diagnostics.Process", "System.Diagnostics.ProcessStartInfo",
    };

    private static readonly HashSet<string> ProcessCreationEntryPoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreateProcess", "CreateProcessW", "CreateProcessA", "CreateProcessAsUser", "CreateProcessAsUserW", "ShellExecute", "ShellExecuteW",
        "ShellExecuteEx", "ShellExecuteExW", "WinExec", "fork", "vfork", "execv", "execve", "execvp", "execl", "execlp", "posix_spawn",
        "posix_spawnp", "system", "popen",
    };

    /// <summary>Copies a fixture repository to a new temporary directory (fixtures themselves stay read-only).</summary>
    public static TempDirectory CopyFixture(string fixture)
    {
        var source = TestRepositoryCatalog.GetFixturePath(fixture);
        var target = new TempDirectory();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target.FullPath, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return target;
    }

    /// <summary>Relative path → SHA-256 of every file below <paramref name="root"/>.</summary>
    public static IReadOnlyDictionary<string, string> Snapshot(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(root, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), StringComparer.Ordinal)
            : new Dictionary<string, string>();

    /// <summary>Describes every difference to the snapshot (new, missing or changed files); empty if unchanged.</summary>
    public static IReadOnlyList<string> Changes(string root, IReadOnlyDictionary<string, string> before)
    {
        var after = Snapshot(root);
        var changes = new List<string>();
        changes.AddRange(after.Keys.Except(before.Keys).Select(f => $"new: {f}"));
        changes.AddRange(before.Keys.Except(after.Keys).Select(f => $"missing: {f}"));
        changes.AddRange(before.Keys.Intersect(after.Keys).Where(f => before[f] != after[f]).Select(f => $"changed: {f}"));
        return changes;
    }

    /// <summary>Marker files below <paramref name="root"/> (non-recursive search is not enough: payloads choose their folder).</summary>
    public static IReadOnlyList<string> Markers(string root) =>
        Directory.Exists(root)
            ? [.. Directory.EnumerateFiles(root, MarkerPrefix + "*", SearchOption.AllDirectories)]
            : [];

    /// <summary>Process-API references in the IL of an assembly (type references and P/Invoke entry points).</summary>
    public static IReadOnlyList<string> ProcessApiUsages(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var usages = new List<string>();

        foreach (var handle in metadata.TypeReferences)
        {
            var type = metadata.GetTypeReference(handle);
            var name = $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
            if (ProcessTypes.Contains(name))
            {
                usages.Add($"type reference {name}");
            }
        }

        foreach (var handle in metadata.MethodDefinitions)
        {
            var import = metadata.GetMethodDefinition(handle).GetImport();
            if (!import.Name.IsNil && ProcessCreationEntryPoints.Contains(metadata.GetString(import.Name)))
            {
                usages.Add($"P/Invoke {metadata.GetString(import.Name)}");
            }
        }

        return usages;
    }
}
