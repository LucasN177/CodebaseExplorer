using System.ComponentModel;
using System.Diagnostics;
using CodebaseExplorer.Application.Settings;

namespace CodebaseExplorer.Desktop.Services;

/// <summary>Starts the user's external editor (UI-009).</summary>
public interface IEditorLauncher
{
    /// <summary>Starts the program; false if it could not be started (not found, no permission).</summary>
    bool Launch(EditorLaunch launch);
}

/// <summary>
/// Starts the editor with <see cref="ProcessStartInfo.ArgumentList"/> and <c>UseShellExecute = false</c>: no shell is
/// involved, every argument reaches the program exactly as built by <see cref="EditorCommandTemplate"/> (SECURITY.md §4).
/// The only process the application starts, and only on an explicit user action with a user-configured command.
/// </summary>
public sealed class EditorLauncher : IEditorLauncher
{
    public bool Launch(EditorLaunch launch)
    {
        try
        {
            using var process = Process.Start(CreateStartInfo(launch));
            return process is not null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public static ProcessStartInfo CreateStartInfo(EditorLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var info = new ProcessStartInfo(launch.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in launch.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }
}
