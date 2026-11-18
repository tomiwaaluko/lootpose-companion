namespace LootPose.Companion;

/// <summary>What happened to a match. Saved in the ledger by name.</summary>
public enum MatchOutcome
{
    Counted,
    Ranked,
    NotBattleRoyale,
    PlayedBeforeLinking,
    CouldNotBeRead,
    AfterEventEnded,
    BeforeReset,
}

/// <summary>The spec's "What counts" on top of LED-N's rules, and which bucket each chest goes in.</summary>
public static class MatchRules
{
    /// <summary>"Habanero" is Epic's code name for ranked playlists, as LED-N's GameModes.cs notes.</summary>
    public static bool IsRanked(string? playlist) =>
        playlist?.Contains("Habanero", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Checked in this order: no start time, started before the link, LED-N's mode rule, started at or after the
    /// event end. Counted means count it, with <see cref="Buckets"/>.
    /// </summary>
    public static MatchOutcome Decide(ReplayFacts facts, DateTimeOffset linkedAt, GameData data)
    {
        if (facts.MatchStartUtc is not { } start) return MatchOutcome.CouldNotBeRead;
        if (start < linkedAt) return MatchOutcome.PlayedBeforeLinking;
        if (!facts.CountsByMode) return IsRanked(facts.Playlist) ? MatchOutcome.Ranked : MatchOutcome.NotBattleRoyale;
        if (Bucketing.EventEnded(data, start)) return MatchOutcome.AfterEventEnded;
        return MatchOutcome.Counted;
    }

    /// <summary>
    /// Chests per bucket. A chest's time is the match start plus its time in the replay. A chest opened at or after
    /// the event end is dropped.
    /// </summary>
    public static Dictionary<string, int> Buckets(ReplayFacts facts, GameData data)
    {
        var buckets = new Dictionary<string, int>(StringComparer.Ordinal);
        if (facts.MatchStartUtc is not { } start) return buckets;
        foreach (var chest in facts.Chests)
        {
            var at = start.AddSeconds(chest.TimeSeconds);
            if (Bucketing.EventEnded(data, at)) continue;
            var key = Bucketing.BucketFor(data, at);
            buckets[key] = buckets.GetValueOrDefault(key) + 1;
        }
        return buckets;
    }

    /// <summary>"DefaultSquad" for "Playlist_DefaultSquad": the mode the match list shows.</summary>
    public static string ModeName(string? playlist)
    {
        if (string.IsNullOrEmpty(playlist)) return "Unknown mode";
        return playlist.StartsWith("Playlist_", StringComparison.OrdinalIgnoreCase) ? playlist["Playlist_".Length..] : playlist;
    }
}
