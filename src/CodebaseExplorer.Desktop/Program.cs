using Avalonia;

namespace CodebaseExplorer.Desktop;

internal static class Program
{
    // Avalonia configuration: do not use Avalonia, third-party APIs or SynchronizationContext-reliant
    // code before AppMain is called; things are not initialized yet.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
