using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CodebaseExplorer.Desktop.Converters;

/// <summary>
/// The app's line icons (24 × 24 units, drawn as strokes, own artwork): navigation and section icons. Views draw them
/// with a <c>Path</c> whose <c>Stroke</c> is the current foreground, so they follow the theme and the selection state.
/// </summary>
public static class AppIcons
{
    private static readonly Dictionary<string, string> Data = new(StringComparer.Ordinal)
    {
        ["home"] = "M4,11 L12,4 L20,11 M6,9.5 V20 H18 V9.5 M10,20 V14 H14 V20",
        ["overview"] = "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z",
        ["graph"] = "M4,6 A2,2 0 1 0 8,6 A2,2 0 1 0 4,6 Z M16,6 A2,2 0 1 0 20,6 A2,2 0 1 0 16,6 Z M10,18 A2,2 0 1 0 14,18 A2,2 0 1 0 10,18 Z M8,6 H16 M7,7.8 L10.9,16.2 M17,7.8 L13.1,16.2",
        ["assistant"] = "M11,4 L12.8,9.2 L18,11 L12.8,12.8 L11,18 L9.2,12.8 L4,11 L9.2,9.2 Z M18,3 V7 M16,5 H20",
        ["history"] = "M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 Z M12,7 V12 L15.5,14",
        ["export"] = "M12,4 V15 M7.5,10.5 L12,15 L16.5,10.5 M5,19 H19",
        ["settings"] = "M4,7 H13 M17,7 H20 M15,5 V9 M4,17 H7 M11,17 H20 M9,15 V19",
        ["folder"] = "M3,7 V18 H21 V9 H11 L9,7 Z",
        ["clone"] = "M6,4 V14 M6,14 A2,2 0 1 0 6,18 A2,2 0 1 0 6,14 M18,6 A2,2 0 1 0 18,10 A2,2 0 1 0 18,6 M18,10 C18,14 6,12 6,14",
        ["repository"] = "M5,4 H17 A2,2 0 0 1 19,6 V20 H7 A2,2 0 0 1 5,18 Z M5,18 A2,2 0 0 1 7,16 H19",
    };

    /// <summary>The geometry of an icon; an empty geometry for unknown keys.</summary>
    public static Geometry Get(string? key) =>
        key is not null && Data.TryGetValue(key, out var data) ? StreamGeometry.Parse(data) : new StreamGeometry();

    /// <summary>Binds an icon key to its geometry: <c>Data="{Binding IconKey, Converter={x:Static conv:AppIcons.Converter}}"</c>.</summary>
    public static IValueConverter Converter { get; } = new FuncValueConverter<string?, Geometry>(Get);

    /// <summary>All keys (tests check that every icon parses).</summary>
    public static IReadOnlyCollection<string> Keys => Data.Keys;
}
