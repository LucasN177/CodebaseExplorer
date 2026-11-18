using System.Globalization;
using CodebaseExplorer.Contracts.Repositories;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>One run in the run history list (UI-005).</summary>
public sealed class RunItemViewModel(AnalysisRunDto run) : ViewModelBase
{
    public AnalysisRunDto Run { get; } = run;

    /// <summary>E.g. <c>Partially succeeded · 18.11.2026 10:30</c>.</summary>
    public string Title => $"{RunStatusText.Of(Run.Status)} · {Run.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}";

    /// <summary>E.g. <c>Manual · 2.4 s · 1 error, 3 warnings</c>.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { TriggerText(Run.Trigger) };
            if (Run.Duration is { } duration)
            {
                parts.Add(DurationText(duration));
            }

            parts.Add($"{Count(Run.ErrorCount, "error")}, {Count(Run.WarningCount, "warning")}");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Statistics of a run that analyzed files, e.g. <c>6 projects · 40 files · 12 relationships</c>.</summary>
    public string Statistics =>
        $"{Count(Run.ProjectCount, "project")} · {Count(Run.FilesScanned, "file")} · {Count(Run.RelationshipCount, "relationship")}";

    /// <summary>The analyzed commit, or why there is none.</summary>
    public string CommitText => Run.CommitSha is { } sha
        ? $"Commit {sha[..Math.Min(sha.Length, 12)]}{(Run.Branch is { } branch ? $" on {branch}" : string.Empty)}"
        : "No commit recorded (Git support follows in a later version)";

    public bool IsCurrent => Run.IsCurrent;

    /// <summary>Accessible name of the list item.</summary>
    public override string ToString() => Title;

    internal static string Count(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count.ToString(CultureInfo.CurrentCulture)} {noun}s";

    private static string TriggerText(string trigger) => trigger switch
    {
        "initial" => "Initial import",
        "manual" => "Re-analysis",
        "incremental" => "Incremental",
        _ => trigger,
    };

    private static string DurationText(TimeSpan duration) => duration.TotalSeconds < 60
        ? $"{duration.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)} s"
        : $"{(int)duration.TotalMinutes} min {duration.Seconds} s";
}

/// <summary>One diagnostic of the selected run (UI-005).</summary>
public sealed class DiagnosticItemViewModel(DiagnosticDto diagnostic) : ViewModelBase
{
    public DiagnosticDto Diagnostic { get; } = diagnostic;

    public string SeverityText => Diagnostic.Severity switch
    {
        "error" => "Error",
        "warning" => "Warning",
        _ => "Info",
    };

    public bool IsError => Diagnostic.Severity == "error";

    public string Code => Diagnostic.Code;

    public string Message => Diagnostic.Message;

    /// <summary>Analyzer and location, e.g. <c>dotnet.project-system · src/App/App.csproj:12</c>.</summary>
    public string Origin
    {
        get
        {
            var location = Diagnostic.Path is { } path
                ? Diagnostic.Line is { } line ? $"{path}:{line.ToString(CultureInfo.InvariantCulture)}" : path
                : Diagnostic.Line is { } onlyLine ? $"line {onlyLine.ToString(CultureInfo.InvariantCulture)} (file no longer in the model)" : "no file location";
            return $"{Diagnostic.Analyzer ?? "analysis"} · {location}";
        }
    }
}

/// <summary>Severity filter of the diagnostics list.</summary>
public enum DiagnosticFilter
{
    All,
    Errors,
    Warnings,
    Info,
}
