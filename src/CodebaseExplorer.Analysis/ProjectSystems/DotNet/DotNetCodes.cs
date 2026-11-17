using CodebaseExplorer.Domain.Common;

namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>Analyzer id and diagnostic/rule codes of the static .NET project-system analysis.</summary>
internal static class DotNetCodes
{
    public const string AnalyzerName = "dotnet.msbuild-static";

    public static readonly AnalyzerId Analyzer = AnalyzerId.Create(AnalyzerName);

    // Diagnostics
    public const string SolutionNotRecognized = "dotnet.solution.not-recognized";
    public const string SolutionMalformedEntry = "dotnet.solution.malformed-entry";
    public const string InvalidXml = "dotnet.xml.invalid";
    public const string PathOutsideRepository = "dotnet.path-outside-repository";
    public const string UnsupportedCondition = "dotnet.msbuild.unsupported-condition";
    public const string UnresolvedProperty = "dotnet.msbuild.unresolved-property";
    public const string MissingProject = "dotnet.project.missing";
    public const string MissingPackageVersion = "dotnet.package.version-missing";

    // Evidence rules
    public const string RuleProjectReference = "csproj.ProjectReference";
    public const string RulePackageReference = "csproj.PackageReference";
    public const string RuleFrameworkReference = "csproj.FrameworkReference";
}
