using System.Globalization;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>One line of the source snippet (UI-007): rendered as plain text, never interpreted.</summary>
/// <param name="Number">1-based line number.</param>
/// <param name="Text">Line content.</param>
/// <param name="IsEvidence">True for the lines of the evidence range (highlighted).</param>
public sealed record SnippetLine(int Number, string Text, bool IsEvidence)
{
    public string NumberText => Number.ToString(CultureInfo.InvariantCulture);
}
