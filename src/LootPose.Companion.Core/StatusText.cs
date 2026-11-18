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

    /// <summary>"Last match: 12 chests · Synced" or "Last match: 12 chests · Offline, will retry".</summary>
    public static string LastMatch(int chests, bool synced) =>
        $"Last match: {Chests(chests)} · {(synced ? "Synced" : "Offline, will retry")}";

    /// <summary>"1 chest", "12 chests".</summary>
    public static string Chests(int count) => count == 1 ? "1 chest" : $"{count} chests";
}
