namespace CodebaseExplorer.Desktop.Services;

/// <summary>A message in the shell's notification area (e.g. a failed background operation).</summary>
public sealed record ShellNotification(NotificationSeverity Severity, string Message, DateTimeOffset Time);
