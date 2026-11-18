using System.Text.Json.Nodes;

namespace LootPose.Companion.Tests;

public class LinkTests
{
    private static readonly ServerConfig Server = new(new Uri("https://project.example/"), "test-key");

    private const string NotLinked = """{"ok":false,"error":"not_linked"}""";
    private const string Linked = """{"ok":true,"buckets":{},"revision":0,"epoch":2}""";

    [Fact]
    public void The_fingerprint_and_code_match_the_web_apps_worked_example()
    {
        var secret = Enumerable.Repeat((byte)1, 32).ToArray();
        var fingerprint = LinkCodes.Fingerprint(secret);

        Assert.Equal("72cd6e8422c407fb6d098690f1130b7ded7ec2f7f5e1d30bd9d521f015363793", fingerprint);
        Assert.Equal("72C-D6E", LinkCodes.Code(fingerprint));
        Assert.Equal(
            "https://lootpose.vercel.app/?link=77777777-7777-4777-8777-777777777777&k=" + fingerprint,
            LinkCodes.LinkAddress("77777777-7777-4777-8777-777777777777", fingerprint).ToString());
    }

    [Fact]
    public async Task Asks_every_three_seconds_until_the_player_presses_Link()
    {
        using var folder = new TempFolder();
        var ledger = Ledger.Open(folder.Path);
        var http = new FakeHttp().Reply(NotLinked).Reply(NotLinked).Reply(Linked);
        var now = GameData.Instant("2026-11-21T20:00:00Z");
        var waits = new List<TimeSpan>();
        var linker = new Linker(new DeviceApi(new HttpClient(http), Server), ledger, () => now, (wait, _) =>
        {
            waits.Add(wait);
            now += wait;
            return Task.CompletedTask;
        });

        Assert.Equal(LinkResult.Linked, await linker.WaitAsync(CancellationToken.None));

        Assert.Equal(new[] { TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3) }, waits);
        Assert.True(ledger.IsLinked);
        Assert.Equal(now, ledger.Data.LinkedAt);
        Assert.Equal(2, ledger.Data.Epoch);
        var input = JsonNode.Parse(http.Requests[0].Body!)!["input"]!;
        Assert.Equal(ledger.Data.DeviceId, input["deviceId"]!.GetValue<string>());
        Assert.Equal(ledger.SecretHex(), input["secret"]!.GetValue<string>());
    }

    [Fact]
    public async Task Gives_up_after_ten_minutes()
    {
        using var folder = new TempFolder();
        var ledger = Ledger.Open(folder.Path);
        var http = new FakeHttp().Reply(NotLinked).Reply(NotLinked).Reply(NotLinked);
        var now = GameData.Instant("2026-11-21T20:00:00Z");
        var linker = new Linker(new DeviceApi(new HttpClient(http), Server), ledger, () => now, (_, _) =>
        {
            now += TimeSpan.FromMinutes(4);
            return Task.CompletedTask;
        });

        Assert.Equal(LinkResult.TimedOut, await linker.WaitAsync(CancellationToken.None));

        Assert.Equal(3, http.Requests.Count);
        Assert.False(ledger.IsLinked);
    }

    [Fact]
    public async Task Waits_as_long_as_the_server_asks()
    {
        using var folder = new TempFolder();
        var http = new FakeHttp().Reply("""{"ok":false,"error":"rate_limited","retryAfter":60}""").Reply(Linked);
        var waits = new List<TimeSpan>();
        var linker = new Linker(new DeviceApi(new HttpClient(http), Server), Ledger.Open(folder.Path), () => DateTimeOffset.UtcNow, (wait, _) =>
        {
            waits.Add(wait);
            return Task.CompletedTask;
        });

        Assert.Equal(LinkResult.Linked, await linker.WaitAsync(CancellationToken.None));
        Assert.Equal(new[] { TimeSpan.FromSeconds(60) }, waits);
    }

    [Fact]
    public async Task Keeps_asking_through_a_lost_connection()
    {
        using var folder = new TempFolder();
        var http = new FakeHttp().Fail().Reply(Linked);
        var linker = new Linker(new DeviceApi(new HttpClient(http), Server), Ledger.Open(folder.Path), () => DateTimeOffset.UtcNow, (_, _) => Task.CompletedTask);

        Assert.Equal(LinkResult.Linked, await linker.WaitAsync(CancellationToken.None));
        Assert.Equal(2, http.Requests.Count);
    }
}
