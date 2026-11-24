using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace Hearwood;

/// <summary>
/// Starting Hearwood in the tray when Windows starts. An unpackaged copy uses the per-user Run entry. The Store
/// package can't, because its registry writes are private to it, so it uses the startup task its manifest
/// declares (packaging/AppxManifest.xml), which the user can also turn off in Windows' Startup apps.
/// </summary>
static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Hearwood";

    /// <summary>The startup task's TaskId in the package manifest.</summary>
    const string TaskId = "HearwoodStartup";

    static string Command => $"\"{Environment.ProcessPath}\" --tray";

    /// <summary>Running from an MSIX package (the Store build).</summary>
    public static bool IsPackaged { get; } = DetectPackage();

    static bool DetectPackage()
    {
        int length = 0;
        return Native.GetCurrentPackageFullName(ref length, null) != Native.APPMODEL_ERROR_NO_PACKAGE;
    }

    /// <summary>Whether Hearwood starts with Windows. Unpackaged, that is the user's saved choice.</summary>
    public static async Task<bool> IsOnAsync(bool saved)
    {
        if (!IsPackaged) return saved;
        var task = await StartupTask.GetAsync(TaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    /// <summary>Turns starting with Windows on or off and returns whether it is on now.</summary>
    /// <exception cref="InvalidOperationException">Windows won't allow the change; the message says why.</exception>
    public static async Task<bool> SetAsync(bool on)
    {
        if (!IsPackaged)
        {
            SetRunEntry(on);
            return on;
        }

        var task = await StartupTask.GetAsync(TaskId);
        if (!on)
        {
            task.Disable();
            return task.State is StartupTaskState.EnabledByPolicy;
        }
        return await task.RequestEnableAsync() switch
        {
            StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => true,
            // Only the user can undo this, from Windows' own settings.
            StartupTaskState.DisabledByUser => throw new InvalidOperationException(
                "Hearwood is turned off in Windows' startup apps. Turn it on in Settings > Apps > Startup."),
            StartupTaskState.DisabledByPolicy => throw new InvalidOperationException(
                "A policy on this PC stops Hearwood from starting with Windows."),
            _ => false,
        };
    }

    /// <summary>Windows started the package at sign-in, so only the tray icon should appear (as --tray does unpackaged).</summary>
    public static bool StartedAtSignIn()
    {
        if (!IsPackaged) return false;
        try { return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask; }
        catch (Exception) { return false; }
    }

    static void SetRunEntry(bool on)
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("The Windows startup list cannot be opened.");
        if (on) run.SetValue(Name, Command);
        else run.DeleteValue(Name, throwOnMissingValue: false);
    }

    /// <summary>Points an existing Run entry at this exe, so moving the app folder does not break starting with Windows.</summary>
    public static void Refresh()
    {
        if (IsPackaged) return;
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(Name) is string current && current != Command)
                run.SetValue(Name, Command);
        }
        catch (Exception) { /* not worth bothering the user at startup */ }
    }
}
