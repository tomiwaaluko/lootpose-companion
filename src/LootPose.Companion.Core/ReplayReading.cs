using ChestCounter;

namespace LootPose.Companion;

/// <summary>An earlier read is still running. Try again a little later.</summary>
public sealed class ReplayBusyException() : Exception("Another replay is still being read.");

/// <summary>
/// Runs LED-N's analyzer the way LED-N's Tracker does: the whole file is read with sharing (Fortnite may still hold
/// it), parsed on a lowest-priority thread, and given up after 3 minutes.
/// </summary>
public static class ReplayReading
{
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(3);

    private static readonly object Gate = new();
    private static Thread? _worker;

    // LED-N's texts default to French. The companion shows none of them; English keeps any message readable.
    static ReplayReading() => L.Set("en");

    public static ReplayFacts Read(string path) => Read(path, Limit);

    public static ReplayFacts Read(string path, TimeSpan limit)
    {
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }

        MatchRecord? result = null;
        Exception? failure = null;
        Thread worker;
        lock (Gate)
        {
            // LED-N's reader keeps some state in static fields, so one replay is read at a time. A read that ran out
            // of time may still be running: it must end before the next one starts.
            if (_worker is { IsAlive: true }) throw new ReplayBusyException();
            worker = new Thread(() =>
            {
                try
                {
                    using var memory = new MemoryStream(bytes, writable: false);
                    // An empty name tracks the player who recorded the replay, as LED-N's Tracker does.
                    result = new ReplayAnalyzer().Analyze(memory, Path.GetFileName(path), "");
                }
                catch (Exception e)
                {
                    failure = e;
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.Lowest,
                Name = "ReplayAnalysis",
            };
            _worker = worker;
            worker.Start();
        }

        if (!worker.Join(limit)) throw new TimeoutException("Reading the replay took more than 3 minutes.");
        if (failure is not null) throw new InvalidDataException("The replay could not be read.", failure);
        return ReplayFacts.From(result!);
    }
}
