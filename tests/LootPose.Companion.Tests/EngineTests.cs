namespace LootPose.Companion.Tests;

public class EngineTests
{
    private static readonly GameData Schedule = GameData.Parse("""
        {"weeklyReset":{"day":"sunday","timeUtc":"14:00"},"eventEnd":"2026-12-05T19:00:00Z",
         "treasureHours":[{"id":"th","start":"2026-11-21T19:00:00Z","end":"2026-11-21T21:00:00Z"}]}
        """);

    private static DateTime Utc(string text) => GameData.Instant(text).UtcDateTime;

    /// <summary>A match with three chests, 10, 20 and 30 seconds after its start.</summary>
    private static Func<ReplayFacts> Match(string session, string start, string playlist = "Playlist_DefaultSquad", bool counts = true) =>
        () => new ReplayFacts(session, GameData.Instant(start), playlist, counts,
            new[] { new ChestFact(10, false), new ChestFact(20, false), new ChestFact(30, false) });

    /// <summary>A linked PC (since 2026-11-20 00:00 UTC), a Demos folder in memory, and the clock at 2026-11-21 20:00 UTC.</summary>
    private sealed class Setup : IDisposable
    {
        private readonly TempFolder _folder = new();

        public Setup(bool linked = true)
        {
            Ledger = linked ? TestLedgers.Linked(_folder) : Ledger.Open(_folder.Path);
            Sync = new FakeSync(Ledger);
            Make("1.0.0");
        }

        public Ledger Ledger { get; }

        public FakeReplayFiles Files { get; } = new();

        public FakeData Data { get; } = new(Schedule);

        public FakeSync Sync { get; }

        public CompanionEngine Engine { get; private set; } = null!;

        public DateTimeOffset Now { get; set; } = GameData.Instant("2026-11-21T20:00:00Z");

        public bool FortniteRunning { get; set; }

        /// <summary>A new engine over the same ledger and folder, as after a restart or an update.</summary>
        public void Make(string appVersion) =>
            Engine = new CompanionEngine(Ledger, Files, Data, Sync, () => Now, () => FortniteRunning, appVersion);

        public async Task<EngineView> TickAsync()
        {
            await Engine.TickAsync(CancellationToken.None);
            return Engine.View;
        }

        public void Dispose() => _folder.Dispose();
    }

    [Fact]
    public void The_last_match_line_reads_as_the_spec_writes_it()
    {
        Assert.Equal("Last match: 12 chests · Synced", StatusText.LastMatch(12, synced: true));
        Assert.Equal("Last match: 1 chest · Offline, will retry", StatusText.LastMatch(1, synced: false));
    }

    [Fact]
    public async Task Before_linking_no_replay_is_read_and_the_window_shows_a_code()
    {
        using var s = new Setup(linked: false);
        s.Files.Add("a.replay", Utc("2026-11-21T19:30:00Z"), Utc("2026-11-21T19:50:00Z"), Match("a", "2026-11-21T19:30:00Z"));

        var view = await s.TickAsync();

        Assert.Equal("Not linked", view.Status);
        Assert.False(view.Linked);
        Assert.Matches("^[0-9A-F]{3}-[0-9A-F]{3}$", view.Code);
        Assert.Empty(s.Files.Reads);
    }

    [Fact]
    public async Task A_finished_match_after_the_link_is_counted_once_and_synced()
    {
        using var s = new Setup();
        s.Files.Add("a.replay", Utc("2026-11-21T19:30:00Z"), Utc("2026-11-21T19:50:00Z"), Match("session-1", "2026-11-21T19:30:00Z"));
        s.Files.Add("copy.replay", Utc("2026-11-21T19:30:00Z"), Utc("2026-11-21T19:51:00Z"), Match("session-1", "2026-11-21T19:30:00Z"));

        var view = await s.TickAsync();

        Assert.Equal("Last match: 3 chests · Synced", view.Status);
        Assert.True(view.Linked);
        Assert.Null(view.Code);
        Assert.Equal(new Dictionary<string, int> { ["2026-11-15|th"] = 3 }, s.Ledger.Buckets());
        Assert.Single(s.Ledger.Data.Matches);
        Assert.Equal(1, s.Sync.Calls);

        await s.TickAsync();
        Assert.Equal(2, s.Files.Reads.Count);
        Assert.Equal(1, s.Sync.Calls);
    }

    [Fact]
    public async Task Replays_from_before_the_link_are_listed_without_being_read()
    {
        using var s = new Setup();
        s.Files.Add("old.replay", Utc("2026-11-19T18:00:00Z"), Utc("2026-11-19T18:25:00Z"), Match("old", "2026-11-19T18:00:00Z"));
        s.Files.Add("during.replay", Utc("2026-11-19T23:50:00Z"), Utc("2026-11-20T00:10:00Z"), Match("during", "2026-11-19T23:50:00Z"));

        var view = await s.TickAsync();

        Assert.Equal(new[] { @"C:\Demos\during.replay" }, s.Files.Reads);
        Assert.Equal(2, s.Ledger.Data.Matches.Count);
        Assert.All(s.Ledger.Data.Matches, m => Assert.Equal(MatchOutcome.PlayedBeforeLinking, m.Outcome));
        Assert.Empty(s.Ledger.Buckets());
        Assert.Equal("Waiting for a match", view.Status);
    }

    [Fact]
    public async Task Ranked_and_other_modes_add_nothing()
    {
        using var s = new Setup();
        s.Files.Add("r.replay", Utc("2026-11-21T19:00:00Z"), Utc("2026-11-21T19:30:00Z"), Match("r", "2026-11-21T19:00:00Z", "Playlist_HabaneroSquad", counts: false));
        s.Files.Add("c.replay", Utc("2026-11-21T19:31:00Z"), Utc("2026-11-21T19:45:00Z"), Match("c", "2026-11-21T19:31:00Z", "Playlist_PlaygroundV2", counts: false));

        var view = await s.TickAsync();

        Assert.Equal(new[] { MatchOutcome.Ranked, MatchOutcome.NotBattleRoyale }, s.Ledger.Data.Matches.Select(m => m.Outcome));
        Assert.Null(s.Ledger.Data.Pending);
        Assert.Equal("Waiting for a match", view.Status);
    }

    [Fact]
    public async Task An_unreadable_replay_waits_for_a_new_version_of_the_companion()
    {
        using var s = new Setup();
        s.Files.Add("bad.replay", Utc("2026-11-21T19:00:00Z"), Utc("2026-11-21T19:30:00Z"));

        var view = await s.TickAsync();
        Assert.Equal("This Fortnite version isn't supported yet. Update the companion.", view.Status);
        Assert.Equal(MatchOutcome.CouldNotBeRead, s.Ledger.Data.Matches.Single().Outcome);

        await s.TickAsync();
        Assert.Single(s.Files.Reads);

        s.Files.Contents[@"C:\Demos\bad.replay"] = Match("bad", "2026-11-21T19:00:00Z");
        s.Make("1.0.1");
        view = await s.TickAsync();

        Assert.Equal(2, s.Files.Reads.Count);
        Assert.Equal(MatchOutcome.Counted, s.Ledger.Data.Matches.Single().Outcome);
        Assert.Equal("Last match: 3 chests · Synced", view.Status);
    }

    [Fact]
    public async Task Without_LootPose_data_a_finished_replay_waits()
    {
        using var s = new Setup();
        s.Data.Value = null;
        s.Files.Add("a.replay", Utc("2026-11-21T19:30:00Z"), Utc("2026-11-21T19:50:00Z"), Match("a", "2026-11-21T19:30:00Z"));

        var view = await s.TickAsync();
        Assert.Equal("Waiting for LootPose data.", view.Status);
        Assert.Empty(s.Files.Reads);

        s.Data.Value = Schedule;
        view = await s.TickAsync();
        Assert.Equal("Last match: 3 chests · Synced", view.Status);
    }

    [Fact]
    public async Task A_replay_still_being_recorded_shows_a_match_in_progress()
    {
        using var s = new Setup();
        var file = s.Files.Add("live.replay", Utc("2026-11-21T19:45:00Z"), Utc("2026-11-21T19:59:50Z"));
        s.Files.Recording.Add(file.Path);

        var view = await s.TickAsync();

        Assert.Equal("Match in progress", view.Status);
        Assert.Empty(s.Files.Reads);
    }

    [Fact]
    public async Task A_just_finished_replay_is_read_once_it_has_been_still_for_five_seconds()
    {
        using var s = new Setup();
        s.Files.Add("a.replay", Utc("2026-11-21T19:30:00Z"), Utc("2026-11-21T19:59:58Z"), Match("a", "2026-11-21T19:30:00Z"));

        await s.TickAsync();
        Assert.True(s.Engine.LookSoon);
        Assert.Empty(s.Files.Reads);

        s.Now = s.Now.AddSeconds(6);
        await s.TickAsync();
        Assert.Single(s.Files.Reads);
    }

    [Fact]
    public async Task After_the_event_end_nothing_more_is_counted()
    {
        using var s = new Setup();
        s.Now = GameData.Instant("2026-12-05T20:00:00Z");
        s.Files.Add("late.replay", Utc("2026-12-05T19:10:00Z"), Utc("2026-12-05T19:40:00Z"), Match("late", "2026-12-05T19:10:00Z"));

        var view = await s.TickAsync();

        Assert.Equal("The event has ended.", view.Status);
        Assert.Equal(MatchOutcome.AfterEventEnded, s.Ledger.Data.Matches.Single().Outcome);
        Assert.Empty(s.Ledger.Buckets());
    }

    [Fact]
    public async Task A_count_that_could_not_be_sent_says_it_will_retry()
    {
        using var s = new Setup();
        s.Sync.Next = new SyncResult(SyncState.Offline);
        s.Files.Add("a.replay", Utc("2026-11-21T19:30:00Z"), Utc("2026-11-21T19:50:00Z"), Match("a", "2026-11-21T19:30:00Z"));

        var view = await s.TickAsync();

        Assert.Equal("Last match: 3 chests · Offline, will retry", view.Status);
        Assert.NotNull(s.Ledger.Data.Pending);
    }

    [Fact]
    public async Task The_replay_hint_shows_after_fifteen_minutes_of_Fortnite_with_no_new_replay()
    {
        using var s = new Setup();
        s.FortniteRunning = true;
        Assert.False((await s.TickAsync()).ShowReplayHint);

        s.Now = s.Now.AddMinutes(15);
        Assert.True((await s.TickAsync()).ShowReplayHint);

        var file = s.Files.Add("new.replay", Utc("2026-11-21T20:14:00Z"), Utc("2026-11-21T20:14:30Z"));
        s.Files.Recording.Add(file.Path);
        Assert.False((await s.TickAsync()).ShowReplayHint);
    }
}
