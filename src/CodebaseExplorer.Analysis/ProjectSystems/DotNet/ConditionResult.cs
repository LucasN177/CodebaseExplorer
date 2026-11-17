namespace CodebaseExplorer.Analysis.ProjectSystems.DotNet;

/// <summary>Result of statically evaluating an MSBuild condition.</summary>
internal enum ConditionResult
{
    True,
    False,

    /// <summary>The condition is outside the supported subset or depends on unknown values.</summary>
    Unknown,
}
