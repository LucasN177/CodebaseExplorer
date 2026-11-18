using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CodebaseExplorer.Desktop.Views.Pages;

public sealed partial class ArchitectureExplorerPageView : UserControl
{
    public ArchitectureExplorerPageView()
    {
        InitializeComponent();

        // Ctrl+K (Cmd+K on macOS) moves the keyboard focus to the search box (GRAPH-007, WF-05). View behavior only.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }
}
