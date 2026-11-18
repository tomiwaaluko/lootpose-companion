using System.ComponentModel;
using System.Diagnostics;

namespace LootPose.Companion.App;

internal static class Browser
{
    /// <summary>Opens an address in the player's default browser.</summary>
    public static void Open(string address)
    {
        try
        {
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // No default browser: nothing more to do.
        }
    }
}
