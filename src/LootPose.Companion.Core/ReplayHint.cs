namespace LootPose.Companion;

/// <summary>After Fortnite has run for 15 minutes with no new replay file: "No replay found. Check that Record Replays is on."</summary>
public sealed class ReplayHint
{
    public const string Text = "No replay found. Check that Record Replays is on.";
    public static readonly TimeSpan After = TimeSpan.FromMinutes(15);

    private DateTime? _runningSince;

    public bool Show { get; private set; }

    public void Observe(DateTime nowUtc, bool fortniteRunning, DateTime? newestReplayWriteUtc)
    {
        if (!fortniteRunning)
        {
            _runningSince = null;
            Show = false;
            return;
        }
        _runningSince ??= nowUtc;
        var appeared = newestReplayWriteUtc is { } newest && newest >= _runningSince.Value;
        Show = !appeared && nowUtc - _runningSince.Value >= After;
    }
}
