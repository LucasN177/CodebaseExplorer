using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CodebaseExplorer.Desktop.Composition;
using CodebaseExplorer.Desktop.ViewModels;
using CodebaseExplorer.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.Desktop;

public sealed partial class App : Avalonia.Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _services = CompositionRoot.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainWindowViewModel>(),
            };
            desktop.Exit += (_, _) => _services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
