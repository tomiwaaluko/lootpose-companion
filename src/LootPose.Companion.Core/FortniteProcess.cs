using System.Diagnostics;

namespace LootPose.Companion;

/// <summary>Whether Fortnite is running, from the list of running programs. The process is never opened.</summary>
public static class FortniteProcess
{
    public const string Name = "FortniteClient-Win64-Shipping";

    public static bool IsRunning()
    {
        var found = Process.GetProcessesByName(Name);
        foreach (var process in found)
        {
            process.Dispose();
        }
        return found.Length > 0;
    }
}
