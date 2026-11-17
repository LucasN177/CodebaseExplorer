namespace CodebaseExplorer.Domain.Files;

/// <summary>What a repository file is used for.</summary>
public enum FileClassification
{
    Source,
    Test,
    ProjectManifest,
    SolutionFile,
    Configuration,
    Documentation,
    Generated,
    Binary,
    Other,
}
