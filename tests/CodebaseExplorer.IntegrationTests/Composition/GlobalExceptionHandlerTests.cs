using CodebaseExplorer.Application.Abstractions;
using CodebaseExplorer.Application.Repositories;
using CodebaseExplorer.Desktop.Services;
using CodebaseExplorer.Infrastructure.Logging;
using CodebaseExplorer.TestRepositories;
using Microsoft.Extensions.DependencyInjection;

namespace CodebaseExplorer.IntegrationTests.Composition;

/// <summary>
/// CORE-007: unhandled exceptions produce a log entry in the file log of the real composition root and a
/// message for the user; logs never contain file contents.
/// </summary>
public sealed class GlobalExceptionHandlerTests : IAsyncLifetime
{
    private readonly TestApplication _app = new();

    private GlobalExceptionHandler Handler => _app.Services.GetRequiredService<GlobalExceptionHandler>();

    private ShellStatus Shell => _app.Services.GetRequiredService<ShellStatus>();

    private string LogsDirectory => _app.Services.GetRequiredService<IApplicationPaths>().LogsDirectory;

    public ValueTask InitializeAsync() => _app.InitializeAsync();

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public void UiException_IsLoggedAndShown()
    {
        Handler.HandleUiException(new InvalidOperationException("ui boom"));

        var log = ReadLog();
        Assert.Contains("[ERR] CodebaseExplorer.Desktop.Services.GlobalExceptionHandler: Unhandled exception on the UI thread", log, StringComparison.Ordinal);
        Assert.Contains("ui boom", log, StringComparison.Ordinal);
        var notification = Assert.Single(Shell.Notifications);
        Assert.Equal(NotificationSeverity.Error, notification.Severity);
        Assert.Contains(LogsDirectory, notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FatalException_IsLoggedAndReportedAtTheNextStart()
    {
        Handler.HandleFatalException(new InvalidOperationException("fatal boom"));

        Assert.Contains("[CRT]", ReadLog(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(LogsDirectory, GlobalExceptionHandler.CrashMarkerFileName)));
        Assert.Empty(Shell.Notifications);

        Handler.ReportPreviousUnexpectedExit();

        Assert.Contains("closed unexpectedly", Assert.Single(Shell.Notifications).Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(LogsDirectory, GlobalExceptionHandler.CrashMarkerFileName)));

        Handler.ReportPreviousUnexpectedExit();
        Assert.Single(Shell.Notifications);
    }

    [Fact]
    public void UnobservedTaskException_IsLogged()
    {
        Handler.HandleUnobservedTaskException(new AggregateException(new TimeoutException("late")));

        Assert.Contains("Unobserved task exception", ReadLog(), StringComparison.Ordinal);
        Assert.Empty(Shell.Notifications);
    }

    [Fact]
    public async Task Import_NeverLogsFileContents()
    {
        var fixture = TestRepositoryCatalog.GetFixturePath("dotnet-layered");
        await _app.Services.GetRequiredService<ImportLocalRepository>().ExecuteAsync(fixture, progress: null, TestContext.Current.CancellationToken);
        Handler.HandleUiException(new InvalidOperationException("marker so that the log exists"));

        var log = ReadLog();
        var contentLines = Directory.EnumerateFiles(fixture, "*", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines)
            .Select(line => line.Trim())
            .Where(line => line.Length >= 12)
            .Distinct(StringComparer.Ordinal);
        Assert.All(contentLines, line => Assert.DoesNotContain(line, log, StringComparison.Ordinal));
    }

    private string ReadLog()
    {
        var path = _app.Services.GetRequiredService<FileLoggerProvider>().CurrentFilePath
            ?? throw new InvalidOperationException("Nothing was logged.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
