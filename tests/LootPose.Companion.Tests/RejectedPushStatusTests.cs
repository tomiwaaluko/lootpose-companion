using System.Text.Json.Nodes;

namespace LootPose.Companion.Tests;

public class RejectedPushStatusTests
{
    private static readonly DateTimeOffset Now = GameData.Instant("2026-11-21T20:00:00Z");
    private static readonly GameData Schedule = GameData.Parse("""
        {"weeklyReset":{"day":"sunday","timeUtc":"14:00"},"eventEnd":"2026-12-05T19:00:00Z","treasureHours":[]}
        """);
    private const string Status = """{"ok":true,"buckets":{},"revision":0,"epoch":0}""";
    private const string Accepted = """{"ok":true,"buckets":{"2026-11-15|normal":5},"revision":1,"epoch":0}""";

    [Theory]
    [InlineData("invalid_counts", "Last match: 4 chests · Offline, will retry")]
    [InlineData("invalid_request", "Last match: 4 chests · Offline, will retry")]
    [InlineData("event_ended", "The event has ended.")]
    public async Task A_terminal_rejection_keeps_its_status_after_idle_checks_and_restart(string error, string expected)
    {
        using var folder = new TempFolder();
        var ledger = Pending(folder);
        var http = new FakeHttp().Reply(Rejected(error)).Reply(Status).Reply(Status);
        var sync = Sync(ledger, http);
        var engine = Engine(ledger, sync);
        await engine.TickAsync(CancellationToken.None);
        var before = engine.View.Status;
        Assert.Null(ledger.Data.Pending);

        // A successful status request does not mean the rejected counts were accepted.
        await sync.SyncAsync(CancellationToken.None);
        var reopened = Ledger.Open(folder.Path);
        var restarted = Engine(reopened, Sync(reopened, http));
        await restarted.TickAsync(CancellationToken.None);

        Assert.Equal(expected, restarted.View.Status);
        Assert.Equal(expected, before);
        Assert.Null(reopened.Data.Pending);
        Assert.Equal(4, reopened.Buckets()["2026-11-15|normal"]);
        Assert.Equal(3, http.Requests.Count);
        Assert.Single(http.Requests.Where(r => JsonNode.Parse(r.Body!)!["input"]!["action"]!.GetValue<string>() == "push"));
    }

    [Theory]
    [InlineData("invalid_counts")]
    [InlineData("invalid_request")]
    [InlineData("event_ended")]
    public async Task A_later_accepted_push_clears_the_saved_rejection(string error)
    {
        using var folder = new TempFolder();
        var ledger = Pending(folder);
        var http = new FakeHttp().Reply(Rejected(error)).Reply(Accepted);
        var sync = Sync(ledger, http);
        await sync.SyncAsync(CancellationToken.None);

        ledger.Record(TestLedgers.Counted("new", "2026-11-15|normal", 1), "new|1|1");
        Assert.Equal(SyncState.Synced, (await sync.SyncAsync(CancellationToken.None)).State);
        var engine = Engine(Ledger.Open(folder.Path));
        await engine.TickAsync(CancellationToken.None);

        Assert.Equal("Last match: 1 chest · Synced", engine.View.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_reset_or_new_link_does_not_keep_an_old_rejection(bool relink)
    {
        using var folder = new TempFolder();
        var ledger = Pending(folder);
        await Sync(ledger, new FakeHttp().Reply(Rejected("event_ended"))).SyncAsync(CancellationToken.None);
        if (relink)
        {
            ledger.Forget();
            ledger.LinkCredentials();
            ledger.CompleteLink(Now, 0, 0);
        }
        else
        {
            ledger.Reset(1, 1);
        }
        var engine = Engine(Ledger.Open(folder.Path));
        await engine.TickAsync(CancellationToken.None);
        Assert.Equal("Waiting for a match", engine.View.Status);
    }

    [Fact]
    public async Task An_existing_ledger_without_rejection_metadata_still_opens_as_synced()
    {
        using var folder = new TempFolder();
        var ledger = Pending(folder);
        ledger.Pushed(ledger.Data.Pending!, 1, 0);
        var path = Path.Combine(folder.Path, "ledger.json");
        var legacy = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        legacy.Remove("pushRejection");
        File.WriteAllText(path, legacy.ToJsonString());

        var reopened = Ledger.Open(folder.Path);
        var engine = Engine(reopened);
        await engine.TickAsync(CancellationToken.None);

        Assert.True(reopened.IsLinked);
        Assert.Equal(1, reopened.Data.Version);
        Assert.Equal(4, reopened.Buckets()["2026-11-15|normal"]);
        Assert.Equal("Last match: 4 chests · Synced", engine.View.Status);
    }

    private static string Rejected(string error) => $$"""{"ok":false,"error":"{{error}}"}""";

    private static Ledger Pending(TempFolder folder)
    {
        var ledger = TestLedgers.Linked(folder);
        ledger.Record(TestLedgers.Counted("old", "2026-11-15|normal", 4), "old|1|1");
        return ledger;
    }

    private static SyncClient Sync(Ledger ledger, FakeHttp http) =>
        new(new DeviceApi(new HttpClient(http), new ServerConfig(new Uri("https://project.example/"), "test-key")), ledger);

    private static CompanionEngine Engine(Ledger ledger, ISyncClient? sync = null) =>
        new(ledger, new FakeReplayFiles(), new FakeData(Schedule), sync, () => Now, () => false, "1.0.0");
}
