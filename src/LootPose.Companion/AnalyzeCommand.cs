using System.Runtime.InteropServices;

namespace LootPose.Companion.App;

/// <summary>LootPoseCompanion.exe --analyze (file): prints what the companion makes of one replay. Sends nothing.</summary>
internal static class AnalyzeCommand
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    public static int Run(string? path)
    {
        // Temporary CI diagnostic: locate the stdout/stderr break before fixing it.
        var beforeOut = GetStdHandle(-11).ToInt64();
        var beforeError = GetStdHandle(-12).ToInt64();
        var attached = AttachConsole(AttachParentProcess);
        var attachError = Marshal.GetLastWin32Error();
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "lootpose-analyze-diagnostics.txt"),
                $"before stdout={beforeOut}; stderr={beforeError}; attached={attached}; error={attachError}; " +
                $"after stdout={GetStdHandle(-11).ToInt64()}; stderr={GetStdHandle(-12).ToInt64()}; " +
                $"managed stderr null={ReferenceEquals(Console.OpenStandardError(), Stream.Null)}");
        }
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
