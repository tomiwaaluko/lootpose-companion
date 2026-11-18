using ChestCounter;

namespace LootPose.Companion.Tests;

public class MatchRulesTests
{
    private static readonly GameData Schedule = GameData.Parse("""
        {"weeklyReset":{"day":"sunday","timeUtc":"14:00"},"eventEnd":"2026-12-05T19:00:00Z",
         "treasureHours":[{"id":"th","start":"2026-11-21T19:00:00Z","end":"2026-11-21T21:00:00Z"}]}
        """);

    private static readonly DateTimeOffset LinkedAt = GameData.Instant("2026-11-20T00:00:00Z");

    private static ReplayFacts Match(string? start, string playlist = "Playlist_DefaultSquad", bool counts = true, params double[] chestSeconds) =>
        new("session", start is null ? (DateTimeOffset?)null : GameData.Instant(start), playlist, counts,
            chestSeconds.Select(seconds => new ChestFact(seconds, false)).ToList());

    [Fact]
    public void Facts_are_taken_from_LED_Ns_match_record()
    {
        var record = new MatchRecord
        {
            SessionId = "abc",
            MatchStartUtc = new DateTime(2026, 11, 21, 18, 0, 0, DateTimeKind.Unspecified),
            Playlist = "Playlist_DefaultSquad",
            Map = "/Delilah/Maps/Delilah_Terrain",
            Chests =
            {
                new ChestRecord { TimeSeconds = 12.5f, Kind = "Coffre" },
                new ChestRecord { TimeSeconds = 40f, Kind = "Coffre", Destroyed = true },
            },
        };

        var facts = ReplayFacts.From(record);

        Assert.Equal("abc", facts.SessionId);
        Assert.Equal(GameData.Instant("2026-11-21T18:00:00Z"), facts.MatchStartUtc);
        Assert.Equal("Playlist_DefaultSquad", facts.Playlist);
        Assert.True(facts.CountsByMode);
        Assert.Equal(new[] { new ChestFact(12.5, false), new ChestFact(40, true) }, facts.Chests);
    }

    [Fact]
    public void A_Battle_Royale_match_after_the_link_counts()
    {
        Assert.Equal(MatchOutcome.Counted, MatchRules.Decide(Match("2026-11-21T18:00:00Z"), LinkedAt, Schedule));
    }

    [Fact]
    public void A_match_that_started_before_the_link_never_counts()
    {
        Assert.Equal(MatchOutcome.PlayedBeforeLinking, MatchRules.Decide(Match("2026-11-19T23:59:00Z"), LinkedAt, Schedule));
    }

    [Theory]
    [InlineData("Playlist_HabaneroDuo", MatchOutcome.Ranked)]
    [InlineData("Playlist_Figment_Squads", MatchOutcome.NotBattleRoyale)]
    [InlineData("Playlist_PlaygroundV2", MatchOutcome.NotBattleRoyale)]
    public void A_match_LED_N_does_not_count_says_why(string playlist, MatchOutcome expected)
    {
        Assert.Equal(expected, MatchRules.Decide(Match("2026-11-21T18:00:00Z", playlist, counts: false), LinkedAt, Schedule));
    }

    [Fact]
    public void A_match_that_starts_at_the_event_end_does_not_count()
    {
        Assert.Equal(MatchOutcome.AfterEventEnded, MatchRules.Decide(Match("2026-12-05T19:00:00Z"), LinkedAt, Schedule));
    }

    [Fact]
    public void A_replay_with_no_start_time_could_not_be_read()
    {
        Assert.Equal(MatchOutcome.CouldNotBeRead, MatchRules.Decide(Match(null), LinkedAt, Schedule));
    }

    [Fact]
    public void Each_chest_goes_in_the_bucket_of_its_own_time()
    {
        // The match starts a minute before a Treasure Hour: the first chest is before it, the second inside it.
        var buckets = MatchRules.Buckets(Match("2026-11-21T18:59:00Z", "Playlist_DefaultSquad", true, 30, 90), Schedule);
        Assert.Equal(new Dictionary<string, int> { ["2026-11-15|normal"] = 1, ["2026-11-15|th"] = 1 }, buckets);
    }

    [Fact]
    public void Chests_opened_at_or_after_the_event_end_are_dropped()
    {
        var buckets = MatchRules.Buckets(Match("2026-12-05T18:58:00Z", "Playlist_DefaultSquad", true, 60, 120, 300), Schedule);
        Assert.Equal(new Dictionary<string, int> { ["2026-11-29|normal"] = 1 }, buckets);
    }

    [Theory]
    [InlineData("Playlist_DefaultSquad", "DefaultSquad")]
    [InlineData("Custom", "Custom")]
    [InlineData(null, "Unknown mode")]
    public void The_mode_is_shown_without_the_Playlist_prefix(string? playlist, string shown)
    {
        Assert.Equal(shown, MatchRules.ModeName(playlist));
    }

    [Fact]
    public void A_file_that_is_not_a_replay_cannot_be_read()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "broken.replay");
        File.WriteAllBytes(path, new byte[4096]);
        Assert.Throws<InvalidDataException>(() => ReplayReading.Read(path, TimeSpan.FromSeconds(30)));
    }
}
