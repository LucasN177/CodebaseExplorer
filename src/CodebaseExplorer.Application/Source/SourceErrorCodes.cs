namespace CodebaseExplorer.Application.Source;

/// <summary>Stable <see cref="Common.ApplicationError"/> codes of source snippets (UI-007).</summary>
public static class SourceErrorCodes
{
    public const string RangeInvalid = "source.range-invalid";
    public const string FileNotInModel = "source.file-not-in-model";
    public const string FolderMissing = "source.folder-missing";
    public const string FileMissing = "source.file-missing";
    public const string FileUnreadable = "source.file-unreadable";
    public const string LinesMissing = "source.lines-missing";
}
