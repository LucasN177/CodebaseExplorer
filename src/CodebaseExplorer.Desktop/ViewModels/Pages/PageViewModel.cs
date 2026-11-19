namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>A top-level page reachable from the navigation pane.</summary>
public abstract class PageViewModel : ViewModelBase
{
    /// <summary>Title shown in the navigation pane and the page header.</summary>
    public abstract string Title { get; }

    /// <summary>Key of the navigation icon (<c>Converters.AppIcons</c>), e.g. <c>home</c>.</summary>
    public abstract string IconKey { get; }

    /// <summary>
    /// Called by the shell each time the page becomes the current page, to refresh its data. Must not throw:
    /// pages handle and show their own errors.
    /// </summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;

    /// <summary>The title; list items use it as their accessible name (otherwise screen readers read the type name).</summary>
    public override string ToString() => Title;
}
