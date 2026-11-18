using System.Globalization;

namespace LootPose.Companion;

/// <summary>One line of the window's match list.</summary>
public sealed record MatchRow(string When, string Mode, string Result);

public static class MatchRows
{
    /// <summary>The local start time, the mode, and either the chests counted or why the match did not count.</summary>
    public static MatchRow For(MatchEntry entry, TimeZoneInfo zone, CultureInfo culture)
    {
        var when = "";
        if (entry.StartUtc is { } start)
        {
            var local = TimeZoneInfo.ConvertTime(start, zone);
            when = local.ToString("ddd d MMM", culture) + " " + local.ToString("t", culture);
        }
        var mode = entry.Playlist is null ? "" : MatchRules.ModeName(entry.Playlist);
        var result = entry.Outcome == MatchOutcome.Counted ? StatusText.Chests(entry.Chests) : Reason(entry.Outcome);
        return new MatchRow(when, mode, result);
    }

    public static string Reason(MatchOutcome outcome) => outcome switch
    {
        MatchOutcome.Counted => "Counted",
        MatchOutcome.Ranked => "Ranked",
        MatchOutcome.NotBattleRoyale => "Not Battle Royale",
        MatchOutcome.PlayedBeforeLinking => "Played before linking",
        MatchOutcome.CouldNotBeRead => "Could not be read",
        MatchOutcome.AfterEventEnded => "After the event ended",
        MatchOutcome.BeforeReset => "Before a reset",
        _ => "",
    };
}
