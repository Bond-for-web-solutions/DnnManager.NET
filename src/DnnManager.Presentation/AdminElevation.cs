using System.Diagnostics;
using System.Security.Principal;

namespace DnnManager.Presentation;

internal static class AdminElevation
{
    public static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Re-launches the current executable elevated (triggers a UAC prompt) in a new
    /// console window. Returns true if the elevated process was started; the caller
    /// should then exit so the new elevated instance can take over.
    /// </summary>
    public static bool TryRelaunchElevated(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName        = exePath,
                UseShellExecute = true,
                Verb            = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
