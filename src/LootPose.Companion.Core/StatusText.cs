namespace LootPose.Companion;

/// <summary>The spec's status line and other fixed wording, exactly.</summary>
public static class StatusText
{
    public const string NotLinked = "Not linked";
    public const string Waiting = "Waiting for a match";
    public const string InProgress = "Match in progress";
    public const string Counting = "Counting…";
    public const string Unsupported = "This Fortnite version isn't supported yet. Update the companion.";
    public const string EventEnded = "The event has ended.";
    public const string WaitingForData = "Waiting for LootPose data.";
    public const string LinkPrompt = "Link this PC to your LootPose account";
    public const string Linked = "Linked";
    public const string ReplaySetup = "In Fortnite: Settings, Game, Replays, Record Replays: On.";

    /// <summary>Last match with Synced, Offline, will retry, or Not accepted, according to the push state.</summary>
    public static string LastMatch(int chests, SyncState state)
    {
        var text = state switch
        {
            SyncState.Synced => "Synced",
            SyncState.Rejected => "Not accepted",
            _ => "Offline, will retry",
        };
        return $"Last match: {Chests(chests)} · {text}";
    }

    /// <summary>"1 chest", "12 chests".</summary>
    public static string Chests(int count) => count == 1 ? "1 chest" : $"{count} chests";
}
