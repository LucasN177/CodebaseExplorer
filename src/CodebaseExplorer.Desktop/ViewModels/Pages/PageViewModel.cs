namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>A top-level page reachable from the navigation pane.</summary>
public abstract class PageViewModel : ViewModelBase
{
    /// <summary>Title shown in the navigation pane and the page header.</summary>
    public abstract string Title { get; }

    /// <summary>Short text glyph used as navigation icon.</summary>
    public abstract string Glyph { get; }
}
