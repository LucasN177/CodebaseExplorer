namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>A top-level page reachable from the navigation pane.</summary>
public abstract class PageViewModel : ViewModelBase
{
    /// <summary>Title shown in the navigation pane and the page header.</summary>
    public abstract string Title { get; }

    /// <summary>Short text glyph used as navigation icon.</summary>
    public abstract string Glyph { get; }

    /// <summary>
    /// Called by the shell each time the page becomes the current page, to refresh its data. Must not throw:
    /// pages handle and show their own errors.
    /// </summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;
}
