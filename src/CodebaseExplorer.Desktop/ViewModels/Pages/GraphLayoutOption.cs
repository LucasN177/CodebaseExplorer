using CodebaseExplorer.Desktop.Services;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>An entry of the layout switcher in the Architecture Explorer.</summary>
public sealed record GraphLayoutOption(GraphLayout Layout, string DisplayName)
{
    public override string ToString() => DisplayName;
}
