namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>
/// Start page. Repository import (local folder, GitHub/GitLab URL) is added by the REPO-* and UI-* tasks.
/// </summary>
public sealed class StartPageViewModel : PageViewModel
{
    public override string Title => "Start";

    public override string Glyph => "⌂";
}
