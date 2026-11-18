using System.Globalization;

namespace LootPose.Companion;

/// <summary>What LootPoseCompanion.exe --analyze prints. Nothing is sent.</summary>
public static class AnalyzeReport
{
    public static IReadOnlyList<string> Lines(ReplayFacts facts, GameData? data)
    {
        var lines = new List<string>
        {
            "Match start (UTC): " + (facts.MatchStartUtc is { } start ? Time(start, "yyyy-MM-dd HH:mm:ss") : "unknown"),
            "Mode: " + MatchRules.ModeName(facts.Playlist),
            $"Chests in the replay: {facts.Chests.Count} ({facts.Chests.Count(c => c.Destroyed)} destroyed)",
        };
        foreach (var chest in facts.Chests)
        {
            var when = facts.MatchStartUtc is { } matchStart
                ? Time(matchStart.AddSeconds(chest.TimeSeconds), "HH:mm:ss") + " UTC"
                : "+" + chest.TimeSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
            lines.Add($"  {when} {(chest.Destroyed ? "destroyed" : "opened")}");
        }

        if (data is null)
        {
            lines.Add("Counts: unknown. LootPose's data file could not be fetched.");
        }
        else
        {
            // The link time does not apply to a replay checked by hand.
            var outcome = MatchRules.Decide(facts, DateTimeOffset.MinValue, data);
            lines.Add(outcome == MatchOutcome.Counted ? "Counts: yes" : $"Counts: no ({MatchRows.Reason(outcome)})");
            if (outcome == MatchOutcome.Counted)
            {
                var buckets = MatchRules.Buckets(facts, data);
                lines.Add("Chests counted: " + buckets.Values.Sum());
                foreach (var (key, count) in buckets.OrderBy(b => b.Key, StringComparer.Ordinal))
                {
                    lines.Add($"  {key}: {count}");
                }
            }
        }
        lines.Add("Nothing was sent.");
        return lines;
    }

    private static string Time(DateTimeOffset at, string format) => at.UtcDateTime.ToString(format, CultureInfo.InvariantCulture);
}
