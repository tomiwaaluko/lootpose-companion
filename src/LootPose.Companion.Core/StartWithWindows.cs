using System.Security;
using Microsoft.Win32;

namespace LootPose.Companion;

/// <summary>Start with Windows: a value in the user's Run key. Off until the player turns it on.</summary>
public sealed class StartWithWindows
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "LootPose Companion";

    private readonly string _runKeyPath;

    /// <param name="runKeyPath">The tests use their own key. The app uses the real Run key.</param>
    public StartWithWindows(string runKeyPath = RunKey) => _runKeyPath = runKeyPath;

    public bool IsOn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>Starts the exe with --tray (no window) at sign-in. Returns false when Windows refused the change.</summary>
    public bool Set(bool on, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true);
            if (on) key.SetValue(ValueName, $"\"{exePath}\" --tray");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>At each start: when it is on, point it at this exe again, in case the file was moved.</summary>
    public void Refresh(string exePath)
    {
        if (IsOn()) Set(true, exePath);
    }
}
