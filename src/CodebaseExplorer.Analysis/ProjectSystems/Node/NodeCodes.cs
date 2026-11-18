using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.ProjectSystems.Node;

/// <summary>Analyzer id, rule ids, property keys and diagnostic codes of the Node project-system analyzer.</summary>
internal static class NodeCodes
{
    public const string AnalyzerName = "node.manifest";

    public const string Version = "1.0.0";

    public static readonly AnalyzerId Analyzer = AnalyzerId.Create(AnalyzerName);

    public const string PackageJson = "package.json";
    public const string TsConfigJson = "tsconfig.json";
    public const string PnpmWorkspace = "pnpm-workspace.yaml";

    // Rules: "package.json.{dependencyType}" and the tsconfig reference rule.
    public const string RuleTsConfigReference = "tsconfig.references";

    public static string RuleFor(string dependencyType) => $"package.json.{dependencyType}";

    // Project properties
    public const string PropertyVersion = "version";
    public const string PropertyPrivate = "private";
    public const string PropertyWorkspaces = "workspaces";
    public const string PropertyWorkspace = "workspace";
    public const string PropertyScripts = "scripts";
    public const string PropertyLifecycleScripts = "lifecycleScripts";
    public const string PropertyTsConfig = "tsconfig";

    // Diagnostics
    public const string ManifestInvalid = "node.manifest.invalid";
    public const string ManifestEntryIgnored = "node.manifest.entry-ignored";
    public const string NameMissing = "node.package.name-missing";
    public const string DuplicateName = "node.package.duplicate-name";
    public const string LifecycleNotExecuted = "node.scripts.lifecycle-not-executed";
    public const string TsConfigInvalid = "node.tsconfig.invalid";
    public const string TsConfigReferenceUnresolved = "node.tsconfig.reference-unresolved";
    public const string PnpmWorkspaceInvalid = "node.pnpm-workspace.invalid";
}
