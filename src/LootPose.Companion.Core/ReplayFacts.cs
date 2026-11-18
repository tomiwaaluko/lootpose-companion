using ChestCounter;
using System.Globalization;
using System.Security.Cryptography;

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
    /// <summary>Keep real session IDs; replace the vendor's timestamp-filename fallback with a digest of the bytes.</summary>
    public static ReplayFacts From(MatchRecord match, byte[] replayBytes, string fileName)
    {
        var suffix = "-" + Path.GetFileName(fileName);
        var id = match.SessionId;
        var fallback = string.IsNullOrEmpty(id);
        if (!fallback && id.EndsWith(suffix, StringComparison.Ordinal))
        {
            var prefix = id[..^suffix.Length];
            fallback = prefix.Length == 0 || DateTime.TryParseExact(prefix, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out _);
        }
        var facts = From(match);
        return fallback ? facts with { SessionId = "replay:" + Convert.ToHexStringLower(SHA256.HashData(replayBytes)) } : facts;
    }

    /// <summary>From LED-N's analysis. Which matches count is LED-N's rule, GameModes.Counts, unchanged.</summary>
    public static ReplayFacts From(MatchRecord match) => new(
        match.SessionId,
        match.MatchStartUtc is { } start ? (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Utc)) : null,
        match.Playlist,
        GameModes.Counts(match),
        match.Chests.Select(chest => new ChestFact(chest.TimeSeconds, chest.Destroyed)).ToList());
}
