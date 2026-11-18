using CodebaseExplorer.Desktop.Composition;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.ViewModels;

/// <summary>UI-001: background-operation state and notifications shown by the shell.</summary>
public sealed class ShellStatusTests
{
    private readonly ShellStatus _shell = new(TimeProvider.System);

    [Fact]
    public void Idle_IsReadyAndNotBusy()
    {
        Assert.False(_shell.IsBusy);
        Assert.Equal("Ready", _shell.StatusText);
    }

    [Fact]
    public void Operation_ShowsDescriptionAndDetail_UntilDisposed()
    {
        var changed = new List<string?>();
        _shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        using (var operation = _shell.BeginOperation("Analyzing repo"))
        {
            Assert.True(_shell.IsBusy);
            Assert.Equal("Analyzing repo", _shell.StatusText);

            operation.Detail = "Scanning files 3 / 9";
            Assert.Equal("Analyzing repo: Scanning files 3 / 9", _shell.StatusText);
        }

        Assert.False(_shell.IsBusy);
        Assert.Equal("Ready", _shell.StatusText);
        Assert.Contains(nameof(ShellStatus.IsBusy), changed);
        Assert.Contains(nameof(ShellStatus.StatusText), changed);
    }

    [Fact]
    public void ConcurrentOperations_ShowTheLatest_AndStayBusyUntilAllEnd()
    {
        var first = _shell.BeginOperation("First");
        var second = _shell.BeginOperation("Second");

        Assert.Equal("Second", _shell.StatusText);
        second.Dispose();
        Assert.Equal("First", _shell.StatusText);
        Assert.True(_shell.IsBusy);

        first.Dispose();
        first.Dispose(); // idempotent
        Assert.False(_shell.IsBusy);
    }

    [Fact]
    public void Notifications_AreNewestFirst_AndLimited()
    {
        for (var i = 1; i <= ShellStatus.MaxNotifications + 2; i++)
        {
            _shell.Notify(NotificationSeverity.Error, $"error {i}");
        }

        Assert.Equal(ShellStatus.MaxNotifications, _shell.Notifications.Count);
        Assert.Equal($"error {ShellStatus.MaxNotifications + 2}", _shell.Notifications[0].Message);
        Assert.Equal("error 3", _shell.Notifications[^1].Message);
    }

    [Fact]
    public void Shell_DismissCommand_RemovesTheNotification()
    {
        using var provider = CompositionRoot.BuildServiceProvider();
        var shell = provider.GetRequiredService<MainWindowViewModel>();
        shell.Shell.Notify(NotificationSeverity.Warning, "first");
        shell.Shell.Notify(NotificationSeverity.Error, "second");

        shell.DismissNotificationCommand.Execute(shell.Shell.Notifications[0]);

        Assert.Equal(["first"], shell.Shell.Notifications.Select(n => n.Message));
    }
}
