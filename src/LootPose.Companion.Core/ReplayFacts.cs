using ChestCounter;

namespace LootPose.Companion;

/// <summary>One chest the player opened or destroyed, at a time within the replay.</summary>
public sealed record ChestFact(double TimeSeconds, bool Destroyed);

/// <summary>What the companion keeps from one replay. It stays on this PC.</summary>
public sealed record ReplayFacts(
    string SessionId,
    DateTimeOffset? MatchStartUtc,
    string? Playlist,
    bool CountsByMode,
    IReadOnlyList<ChestFact> Chests)
{
    public static ReplayFacts From(MatchRecord match, byte[] replayBytes, string fileName) => From(match);

    /// <summary>From LED-N's analysis. Which matches count is LED-N's rule, GameModes.Counts, unchanged.</summary>
    public static ReplayFacts From(MatchRecord match) => new(
        match.SessionId,
        match.MatchStartUtc is { } start ? (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)) : null,
        match.Playlist,
        GameModes.Counts(match),
        match.Chests.Select(chest => new ChestFact(chest.TimeSeconds, chest.Destroyed)).ToList());
}
