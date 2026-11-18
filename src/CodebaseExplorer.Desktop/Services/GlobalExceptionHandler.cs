using System.Globalization;
using CodebaseExplorer.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>
/// Last line of defense for exceptions nobody caught (CORE-007). Installed by <c>App</c> on the Avalonia
/// dispatcher, <see cref="AppDomain.UnhandledException"/> and <see cref="TaskScheduler.UnobservedTaskException"/>.
/// Every case is logged (file log, redacted); the user sees a message:
/// <list type="bullet">
/// <item>UI thread: the exception is marked handled, the app keeps running and shows a notification.</item>
/// <item>Fatal (any other thread, the process ends): a marker file is written next to the logs, and the next
/// start shows a notification that the app closed unexpectedly.</item>
/// <item>Unobserved task exceptions are logged and marked observed (they never end the process in .NET).</item>
/// </list>
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, ShellStatus shell, IApplicationPaths paths, TimeProvider timeProvider)
{
    /// <summary>File in the logs directory that records an unexpected exit until the next start reported it.</summary>
    public const string CrashMarkerFileName = "unexpected-exit.txt";

    private string CrashMarkerPath => Path.Combine(paths.LogsDirectory, CrashMarkerFileName);

    /// <summary>An exception on the UI thread; the caller marks it handled. Must be called on the UI thread.</summary>
    public void HandleUiException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        logger.LogError(exception, "Unhandled exception on the UI thread");
        shell.Notify(NotificationSeverity.Error,
            $"An unexpected error occurred. The application keeps running; details are in the log files in {paths.LogsDirectory}.");
    }

    /// <summary>An exception that ends the process. May be called on any thread; never touches the UI.</summary>
    public void HandleFatalException(object? exceptionObject)
    {
        if (exceptionObject is Exception exception)
        {
            logger.LogCritical(exception, "Unhandled exception; the application terminates");
        }
        else
        {
            logger.LogCritical("Unhandled non-exception object of type {Type}; the application terminates", exceptionObject?.GetType().FullName);
        }

        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            File.WriteAllText(CrashMarkerPath, timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception markerError) when (markerError is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(markerError, "The unexpected-exit marker could not be written");
        }
    }

    /// <summary>An exception while starting up; the caller shows the startup error window instead of the main window.</summary>
    public void HandleStartupException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        logger.LogCritical(exception, "Startup failed unexpectedly");
    }

    /// <summary>A faulted task whose exception was never observed.</summary>
    public void HandleUnobservedTaskException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        logger.LogError(exception, "Unobserved task exception");
    }

    /// <summary>
    /// Shows a notification if the previous run ended with a fatal exception, then forgets it. Called once after
    /// the main window was created, on the UI thread.
    /// </summary>
    public void ReportPreviousUnexpectedExit()
    {
        if (!File.Exists(CrashMarkerPath))
        {
            return;
        }

        shell.Notify(NotificationSeverity.Error,
            $"Codebase Explorer closed unexpectedly the last time. Details are in the log files in {paths.LogsDirectory}.");
        try
        {
            File.Delete(CrashMarkerPath);
        }
        catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(deleteError, "The unexpected-exit marker could not be deleted");
        }
    }
}
