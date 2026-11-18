using System.Runtime.InteropServices;

namespace LootPose.Companion.App;

/// <summary>LootPoseCompanion.exe --analyze (file): prints what the companion makes of one replay. Sends nothing.</summary>
internal static class AnalyzeCommand
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int handle, IntPtr value);

    private static bool Usable(IntPtr handle) =>
        handle != IntPtr.Zero && handle != new IntPtr(-1) && GetFileType(handle) != 0;

    public static int Run(string? path)
    {
        // A WinExe needs its parent's console, but attachment can replace inherited
        // pipe/file handles. Keep redirection working for CI, > files and | more.
        var output = GetStdHandle(-11);
        var error = GetStdHandle(-12);
        var keepOutput = Usable(output);
        var keepError = Usable(error);
        AttachConsole(AttachParentProcess);
        if (keepOutput) SetStdHandle(-11, output);
        if (keepError) SetStdHandle(-12, error);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), Console.OutputEncoding) { AutoFlush = true });
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
