namespace CodebaseExplorer.Application.Settings;

/// <summary>Keys of the <see cref="IUserSettingsStore"/>; every key and its values are listed in DATABASE_SCHEMA.md §4.17.</summary>
public static class UserSettingKeys
{
    /// <summary>Theme choice: <c>system</c>, <c>light</c> or <c>dark</c>. Missing or unknown values mean <c>system</c>.</summary>
    public const string Theme = "appearance.theme";

    /// <summary>Layout of the architecture graph: <c>force</c>, <c>hierarchical</c> or <c>concentric</c>. Missing or unknown values mean <c>force</c>.</summary>
    public const string GraphLayout = "graph.layout";

    /// <summary>AI assistant on/off: <c>true</c> or <c>false</c>. Anything else means off (AI-001).</summary>
    public const string AiEnabled = "ai.enabled";

    /// <summary>AI provider id; only <c>ollama</c> in the MVP.</summary>
    public const string AiProvider = "ai.provider";

    /// <summary>Base URL of the AI provider, e.g. <c>http://localhost:11434</c>.</summary>
    public const string AiEndpoint = "ai.endpoint";

    /// <summary>Model name at the provider; empty when none is selected.</summary>
    public const string AiModel = "ai.model";

    /// <summary>Context window in tokens (invariant integer).</summary>
    public const string AiMaxContextTokens = "ai.max-context-tokens";

    /// <summary>Sampling temperature (invariant number, 0–2).</summary>
    public const string AiTemperature = "ai.temperature";

    /// <summary>Space-separated repository ids allowed to send context to a remote endpoint.</summary>
    public const string AiRemoteConsents = "ai.remote-consents";

    /// <summary>Files above this size (KB, invariant integer) are not parsed (UI-009).</summary>
    public const string AnalysisMaxFileSizeKilobytes = "analysis.max-file-size-kb";

    /// <summary>Scanning stops after this many files (invariant integer).</summary>
    public const string AnalysisMaxFiles = "analysis.max-files";

    /// <summary>Parse time limit per file in seconds (invariant integer).</summary>
    public const string AnalysisParseTimeoutSeconds = "analysis.parse-timeout-seconds";

    /// <summary>Additional excluded folder names, one per line.</summary>
    public const string AnalysisExcludedFolders = "analysis.excluded-folders";

    /// <summary>Git activity: maximum number of commits (invariant integer).</summary>
    public const string GitHistoryMaxCommits = "git.history-max-commits";

    /// <summary>Git activity: period in days, 0 = no date limit (invariant integer).</summary>
    public const string GitHistoryDays = "git.history-days";

    /// <summary>External editor command template, e.g. <c>code --goto "{path}:{line}"</c>; empty = not configured.</summary>
    public const string EditorCommand = "editor.command";
}
