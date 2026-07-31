using System.Runtime.InteropServices;

namespace IfMonitor;

/// <summary>
/// Windows 10/11 notifications use the Start Menu shortcut icon + AppUserModelID,
/// not NotifyIcon or ShowBalloonTip. Without registration, Action Center shows a generic shield.
/// </summary>
internal static class AppNotificationIdentity
{
    private const string AppUserModelId = "IfMonitor.NetworkMonitor";

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    public static void Register()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch
        {
            // Non-fatal on older Windows.
        }

        RefreshStartMenuShortcut();
    }

    /// <summary>Always rewrite the shortcut so icon path tracks the exe you actually launched.</summary>
    private static void RefreshStartMenuShortcut()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        string shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "IfMonitor.lnk");

        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? exePath;
            shortcut.Description = "IfMonitor";
            shortcut.IconLocation = $"{exePath},0";
            shortcut.Save();
        }
        catch
        {
            // Best-effort; tray monitoring works without the shortcut.
        }
    }
}
