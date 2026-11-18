using CodebaseExplorer.Contracts.Inspector;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CodebaseExplorer.Desktop.ViewModels.Pages;

/// <summary>A checkable filter value (language, ecosystem or relationship kind) of the explorer (GRAPH-007).</summary>
public sealed partial class FilterOption(string token, string display, Action changed) : ObservableObject
{
    public string Token { get; } = token;

    public string Display { get; } = display;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary>A choice with a wire token (minimum evidence level, neighborhood direction).</summary>
public sealed record ChoiceOption(string? Token, string Display)
{
    public override string ToString() => Display;
}

/// <summary>One search result of <c>Ctrl/Cmd+K</c> (GRAPH-007).</summary>
public sealed record SearchResultItem(string Label, string KindText, string? Container, ElementReferenceDto Reference)
{
    public string Detail => Container is null ? KindText : $"{KindText} · {Container}";
}
