namespace LootPose.Companion;

public enum Readiness
{
    /// <summary>The key is not written yet: the match is still being played.</summary>
    Recording,

    /// <summary>Finished, but it changed too recently or has not been seen twice yet.</summary>
    Settling,

    Ready,

    /// <summary>The key was never written and the file stopped changing 10 minutes ago: the recording was cut.</summary>
    Abandoned,
}

/// <summary>
/// The spec: a replay is ready when its key is present and the file has not changed for 5 seconds. From LED-N's
/// Tracker: a finished replay untouched for a minute is ready at once, and one whose key was never written but that
/// has not changed for 10 minutes was cut short (Fortnite writes to a replay about every 25 seconds).
/// </summary>
public static class ReplayReadiness
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan OldReplay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(10);

    /// <param name="sameAsLastLook">The file had the same size and write time at the previous look.</param>
    public static Readiness Check(bool keyMissing, DateTime lastWriteUtc, bool sameAsLastLook, DateTime nowUtc)
    {
        var quietFor = nowUtc - lastWriteUtc;
        if (keyMissing) return quietFor > GiveUpAfter ? Readiness.Abandoned : Readiness.Recording;
        if (quietFor >= OldReplay) return Readiness.Ready;
        return sameAsLastLook && quietFor >= Quiet ? Readiness.Ready : Readiness.Settling;
    }
}
