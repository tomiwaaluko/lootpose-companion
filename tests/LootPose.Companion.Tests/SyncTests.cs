using System.Net;
using System.Text.Json.Nodes;

namespace LootPose.Companion.Tests;

public class SyncTests
{
    private static readonly ServerConfig Server = new(new Uri("https://project.example/"), "test-key");

    private const string Ok = """{"ok":true,"buckets":{"2026-11-15|normal":4},"revision":1,"epoch":0}""";
    private const string Conflict = """{"ok":false,"error":"conflict"}""";

    private static JsonNode Input(FakeHttp http, int index) => JsonNode.Parse(http.Requests[index].Body!)!["input"]!;

    /// <summary>A linked ledger with one counted match waiting to be pushed, and a sync client over fake HTTP.</summary>
    private static (Ledger Ledger, FakeHttp Http, SyncClient Sync) Setup(TempFolder folder, params string[] replies)
    {
        var ledger = TestLedgers.Linked(folder);
        ledger.Record(TestLedgers.Counted("s1", "2026-11-15|normal", 4), "a|1|1");
        var http = new FakeHttp();
        foreach (var reply in replies)
        {
            if (reply == "offline") http.Fail();
            else http.Reply(reply);
        }
        return (ledger, http, new SyncClient(new DeviceApi(new HttpClient(http), Server), ledger));
    }

    [Fact]
    public async Task Status_posts_the_input_with_the_key_in_the_apikey_header()
    {
        var http = new FakeHttp().Reply("""{"ok":false,"error":"not_linked"}""");
        var api = new DeviceApi(new HttpClient(http), Server);

        var reply = await api.StatusAsync("77777777-7777-4777-8777-777777777777", new string('0', 64), CancellationToken.None);

        Assert.False(reply.Ok);
        Assert.Equal("not_linked", reply.Error);
        var sent = http.Requests.Single();
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://project.example/rest/v1/rpc/device_request", sent.Uri!.ToString());
        Assert.Equal("test-key", sent.Headers["apikey"]);
        Assert.False(sent.Headers.ContainsKey("Authorization"));
        Assert.StartsWith("application/json", sent.Headers["Content-Type"]);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"input":{"action":"status","deviceId":"77777777-7777-4777-8777-777777777777","secret":"0000000000000000000000000000000000000000000000000000000000000000"}}"""),
            JsonNode.Parse(sent.Body!)));
    }

    [Fact]
    public async Task Push_sends_the_whole_row_and_reads_the_new_revision()
    {
        var http = new FakeHttp().Reply(Ok);
        var api = new DeviceApi(new HttpClient(http), Server);

        var reply = await api.PushAsync("d", "s", "r", 0, 0, new Dictionary<string, int> { ["2026-11-15|normal"] = 4 }, CancellationToken.None);

        Assert.True(reply.Ok);
        Assert.Equal(1, reply.Revision);
        Assert.Equal(4, reply.Buckets["2026-11-15|normal"]);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{"input":{"action":"push","deviceId":"d","secret":"s","requestId":"r","expectedRevision":0,"epoch":0,"buckets":{"2026-11-15|normal":4}}}"""),
            JsonNode.Parse(http.Requests[0].Body!)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"ok":true}""")]
    public void A_reply_that_is_not_the_promised_shape_counts_as_unavailable(string body)
    {
        var reply = DeviceApi.Parse(body);
        Assert.False(reply.Ok);
        Assert.Equal("unavailable", reply.Error);
    }

    [Fact]
    public void A_failure_keeps_its_code_and_how_long_to_wait()
    {
        var reply = DeviceApi.Parse("""{"ok":false,"error":"rate_limited","retryAfter":60}""");
        Assert.Equal("rate_limited", reply.Error);
        Assert.Equal(60, reply.RetryAfter);
    }

    [Fact]
    public async Task Any_HTTP_status_but_200_means_the_request_did_not_reach_the_function()
    {
        var http = new FakeHttp().Reply("""{"code":"PGRST202"}""", HttpStatusCode.NotFound);
        var api = new DeviceApi(new HttpClient(http), Server);
        await Assert.ThrowsAsync<DeviceApiException>(() => api.StatusAsync("d", "s", CancellationToken.None));
    }

    [Fact]
    public async Task A_lost_reply_is_sent_again_with_the_same_request_id()
    {
        using var folder = new TempFolder();
        var (ledger, http, sync) = Setup(folder, "offline", Ok);

        Assert.Equal(SyncState.Offline, (await sync.SyncAsync(CancellationToken.None)).State);
        Assert.NotNull(ledger.Data.Pending);
        Assert.Equal(SyncState.Synced, (await sync.SyncAsync(CancellationToken.None)).State);

        Assert.Equal(Input(http, 0)["requestId"]!.GetValue<string>(), Input(http, 1)["requestId"]!.GetValue<string>());
        Assert.Null(ledger.Data.Pending);
        Assert.Equal(1, ledger.Data.Revision);
    }

    [Fact]
    public async Task A_conflict_in_the_same_epoch_adopts_the_row_revision_and_pushes_again()
    {
        using var folder = new TempFolder();
        var (ledger, http, sync) = Setup(folder,
            Conflict,
            """{"ok":true,"buckets":{},"revision":5,"epoch":0}""",
            """{"ok":true,"buckets":{"2026-11-15|normal":4},"revision":6,"epoch":0}""");

        Assert.Equal(SyncState.Synced, (await sync.SyncAsync(CancellationToken.None)).State);

        Assert.Equal(new[] { "push", "status", "push" }, Enumerable.Range(0, 3).Select(i => Input(http, i)["action"]!.GetValue<string>()));
        Assert.Equal(5, Input(http, 2)["expectedRevision"]!.GetValue<int>());
        Assert.Equal(6, ledger.Data.Revision);
        Assert.Null(ledger.Data.Pending);
    }

    [Fact]
    public async Task A_conflict_with_a_higher_epoch_means_the_account_was_reset()
    {
        using var folder = new TempFolder();
        var (ledger, http, sync) = Setup(folder, Conflict, """{"ok":true,"buckets":{},"revision":9,"epoch":1}""");

        Assert.Equal(SyncState.Synced, (await sync.SyncAsync(CancellationToken.None)).State);

        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(MatchOutcome.BeforeReset, ledger.Data.Matches.Single().Outcome);
        Assert.Equal((1, 9), (ledger.Data.Epoch, ledger.Data.Revision));
        Assert.Empty(ledger.Buckets());
        Assert.Null(ledger.Data.Pending);
    }

    [Fact]
    public async Task Not_linked_forgets_the_link_and_keeps_the_matches()
    {
        using var folder = new TempFolder();
        var (ledger, _, sync) = Setup(folder, """{"ok":false,"error":"not_linked"}""");

        Assert.Equal(SyncState.NotLinked, (await sync.SyncAsync(CancellationToken.None)).State);

        Assert.False(ledger.IsLinked);
        Assert.Single(ledger.Data.Matches);
    }

    [Fact]
    public async Task Rate_limited_says_how_long_to_wait()
    {
        using var folder = new TempFolder();
        var (_, _, sync) = Setup(folder, """{"ok":false,"error":"rate_limited","retryAfter":60}""");

        var result = await sync.SyncAsync(CancellationToken.None);

        Assert.Equal(SyncState.RateLimited, result.State);
        Assert.Equal(TimeSpan.FromSeconds(60), result.Wait);
    }

    [Fact]
    public void Back_off_doubles_up_to_fifteen_minutes_and_starts_again_after_a_success()
    {
        var backoff = new Backoff();
        var now = GameData.Instant("2026-11-21T20:00:00Z");
        Assert.True(backoff.Allows(now));

        var waits = new List<int>();
        for (var i = 0; i < 7; i++)
        {
            backoff.Failed(now);
            waits.Add((int)(backoff.NotBefore!.Value - now).TotalSeconds);
        }
        Assert.Equal(new[] { 30, 60, 120, 240, 480, 900, 900 }, waits);
        Assert.False(backoff.Allows(now));

        backoff.Succeeded();
        Assert.True(backoff.Allows(now));
        backoff.Failed(now, atLeast: TimeSpan.FromSeconds(60));
        Assert.Equal(TimeSpan.FromSeconds(60), backoff.NotBefore!.Value - now);
    }
}
