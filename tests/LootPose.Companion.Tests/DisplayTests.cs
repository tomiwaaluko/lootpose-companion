using System.Globalization;

namespace LootPose.Companion.Tests;

public class DisplayTests
{
    private static readonly GameData Schedule = GameData.Parse("""
        {"weeklyReset":{"day":"sunday","timeUtc":"14:00"},"eventEnd":"2026-12-05T19:00:00Z",
         "treasureHours":[{"id":"th","start":"2026-11-21T19:00:00Z","end":"2026-11-21T21:00:00Z"}]}
        """);

    [Fact]
    public void The_match_list_shows_the_time_the_mode_and_the_chests_or_why_not()
    {
        var entry = new MatchEntry
        {
            Key = "s",
            StartUtc = GameData.Instant("2026-11-21T19:30:00Z"),
            Playlist = "Playlist_DefaultSquad",
            Outcome = MatchOutcome.Counted,
            Chests = 12,
        };
        Assert.Equal(new MatchRow("Sat 21 Nov 19:30", "DefaultSquad", "12 chests"), MatchRows.For(entry, TimeZoneInfo.Utc, CultureInfo.InvariantCulture));

        entry.Chests = 1;
        Assert.Equal("1 chest", MatchRows.For(entry, TimeZoneInfo.Utc, CultureInfo.InvariantCulture).Result);

        var reasons = new Dictionary<MatchOutcome, string>
        {
            [MatchOutcome.Ranked] = "Ranked",
            [MatchOutcome.NotBattleRoyale] = "Not Battle Royale",
            [MatchOutcome.PlayedBeforeLinking] = "Played before linking",
            [MatchOutcome.CouldNotBeRead] = "Could not be read",
            [MatchOutcome.AfterEventEnded] = "After the event ended",
            [MatchOutcome.BeforeReset] = "Before a reset",
        };
        foreach (var (outcome, text) in reasons)
        {
            entry.Outcome = outcome;
            Assert.Equal(text, MatchRows.For(entry, TimeZoneInfo.Utc, CultureInfo.InvariantCulture).Result);
        }
    }

    [Fact]
    public void Times_are_shown_in_the_PCs_time_zone()
    {
        var fiveHoursBehind = TimeZoneInfo.CreateCustomTimeZone("Test-5", TimeSpan.FromHours(-5), "Test-5", "Test-5");
        var entry = new MatchEntry { StartUtc = GameData.Instant("2026-11-21T19:30:00Z"), Outcome = MatchOutcome.Counted, Chests = 2 };
        Assert.Equal("Sat 21 Nov 14:30", MatchRows.For(entry, fiveHoursBehind, CultureInfo.InvariantCulture).When);
    }

    [Fact]
    public void Analyze_prints_the_start_the_mode_each_chest_whether_it_counts_and_the_buckets()
    {
        var facts = new ReplayFacts("s", GameData.Instant("2026-11-21T18:59:00Z"), "Playlist_DefaultSquad", true,
            new[] { new ChestFact(30, false), new ChestFact(90, true) });

        Assert.Equal(new[]
        {
            "Match start (UTC): 2026-11-21 18:59:00",
            "Mode: DefaultSquad",
            "Chests in the replay: 2 (1 destroyed)",
            "  18:59:30 UTC opened",
            "  19:00:30 UTC destroyed",
            "Counts: yes",
            "Chests counted: 2",
            "  2026-11-15|normal: 1",
            "  2026-11-15|th: 1",
            "Nothing was sent.",
        }, AnalyzeReport.Lines(facts, Schedule));
    }

    [Fact]
    public void Analyze_says_why_a_match_does_not_count_and_when_it_cannot_tell()
    {
        var ranked = new ReplayFacts("s", GameData.Instant("2026-11-21T18:59:00Z"), "Playlist_HabaneroDuo", false, Array.Empty<ChestFact>());

        Assert.Contains("Counts: no (Ranked)", AnalyzeReport.Lines(ranked, Schedule));
        var withoutData = AnalyzeReport.Lines(ranked, null);
        Assert.Contains("Counts: unknown. LootPose's data file could not be fetched.", withoutData);
        Assert.Equal("Nothing was sent.", withoutData[^1]);
    }
}
