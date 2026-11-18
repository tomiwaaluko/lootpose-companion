using System.Runtime.InteropServices;

namespace LootPose.Companion.App;

/// <summary>LootPoseCompanion.exe --analyze (file): prints what the companion makes of one replay. Sends nothing.</summary>
internal static class AnalyzeCommand
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    public static int Run(string? path)
    {
        // The exe is a Windows app with no console of its own: print into the one it was started from.
        AttachConsole(AttachParentProcess);
        if (path is null || !File.Exists(path))
        {
            Console.Error.WriteLine("Usage: LootPoseCompanion.exe --analyze <path to a .replay file>");
            return 2;
        }

        ReplayFacts facts;
        try
        {
            facts = ReplayReading.Read(path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or TimeoutException or UnauthorizedAccessException or ReplayBusyException)
        {
            var cause = e.InnerException is null ? "" : " (" + e.InnerException.Message + ")";
            Console.Error.WriteLine("Could not be read: " + e.Message + cause);
            return 1;
        }

        // The public data file is fetched (or the copy on disk used) to work out the buckets. Nothing is sent.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"LootPoseCompanion/{CompanionInfo.VersionText}");
        var data = new GameDataStore(http, CompanionInfo.DataFolder, () => DateTimeOffset.UtcNow)
            .GetAsync(CancellationToken.None).GetAwaiter().GetResult();
        foreach (var line in AnalyzeReport.Lines(facts, data))
        {
            Console.WriteLine(line);
        }
        return 0;
    }
}
