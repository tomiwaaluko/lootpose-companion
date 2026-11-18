namespace LootPose.Companion.Tests;

internal static class TestLedgers
{
    public static readonly DateTimeOffset LinkedAt = GameData.Instant("2026-11-20T00:00:00Z");

    /// <summary>A ledger in <paramref name="folder"/> that is linked since <see cref="LinkedAt"/>.</summary>
    public static Ledger Linked(TempFolder folder, int revision = 0, int epoch = 0)
    {
        var ledger = Ledger.Open(folder.Path);
        ledger.LinkCredentials();
        ledger.CompleteLink(LinkedAt, revision, epoch);
        return ledger;
    }

    public static MatchEntry Counted(string key, string bucket, int chests) => new()
    {
        Key = key,
        StartUtc = LinkedAt.AddDays(1),
        Playlist = "Playlist_DefaultSquad",
        Outcome = MatchOutcome.Counted,
        Chests = chests,
        Buckets = new Dictionary<string, int>(StringComparer.Ordinal) { [bucket] = chests },
        HandledAt = LinkedAt.AddDays(1),
    };
}
