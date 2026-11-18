using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.ProjectSystems.Python;

/// <summary>Analyzer id, rule ids, property keys and diagnostic codes of the Python project-system analyzer.</summary>
internal static class PythonCodes
{
    public const string AnalyzerName = "python.manifest";

    public const string Version = "1.0.0";

    public static readonly AnalyzerId Analyzer = AnalyzerId.Create(AnalyzerName);

    public const string PyProjectToml = "pyproject.toml";
    public const string SetupCfg = "setup.cfg";
    public const string SetupPy = "setup.py";
    public const string RequirementsTxt = "requirements.txt";

    // Rules
    public const string RuleProjectDependencies = "pyproject.project.dependencies";
    public const string RuleOptionalDependencies = "pyproject.project.optional-dependencies";
    public const string RuleDependencyGroups = "pyproject.dependency-groups";
    public const string RulePoetryDependencies = "pyproject.poetry.dependencies";
    public const string RulePoetryDevDependencies = "pyproject.poetry.dev-dependencies";
    public const string RulePoetryGroupDependencies = "pyproject.poetry.group-dependencies";
    public const string RuleRequirements = "requirements.txt";
    public const string RuleSetupCfgInstallRequires = "setup.cfg.install_requires";
    public const string RuleSetupCfgExtrasRequire = "setup.cfg.extras_require";

    // Project properties
    public const string PropertyVersion = "version";
    public const string PropertyRequiresPython = "requiresPython";
    public const string PropertyBuildBackend = "buildBackend";
    public const string PropertyLayout = "layout";
    public const string PropertyManifests = "manifests";
    public const string PropertySetupPy = "setupPy";

    // Relationship attributes
    public const string AttributeVersionSpec = "versionSpec";
    public const string AttributeMarker = "marker";
    public const string AttributeExtra = "extra";
    public const string AttributeGroup = "group";
    public const string AttributeDevOnly = "devOnly";

    // Diagnostics
    public const string ManifestInvalid = "python.manifest.invalid";
    public const string RequirementInvalid = "python.requirement.invalid";
    public const string RequirementUnsupported = "python.requirement.unsupported";
    public const string OptionIgnored = "python.requirements.option-ignored";
    public const string IncludeOutsideRepository = "python.requirements.include-outside-repository";
    public const string IncludeMissing = "python.requirements.include-missing";
    public const string IncludeDepth = "python.requirements.include-too-deep";
    public const string SetupPyNotExecuted = "python.setup-py.not-executed";
}
