using System.ComponentModel;
using System.Diagnostics;

namespace LootPose.Companion.App;

internal static class Program
{
    // One running copy per Windows user: Local\ names belong to the signed-in session.
    private const string InstanceName = @"Local\LootPoseCompanion.Instance";
    private const string ShowName = @"Local\LootPoseCompanion.Show";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--analyze")
        {
            return AnalyzeCommand.Run(args.Length >= 2 ? args[1] : null);
        }

        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
        using var instance = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            show.Set(); // the copy already running shows its window
            return 0;
        }

        try
        {
            using var self = Process.GetCurrentProcess();
            self.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // Normal priority works too.
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(showWindow: Array.IndexOf(args, "--tray") < 0, show));
        return 0;
    }
}
