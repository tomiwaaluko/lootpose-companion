using System.Text.Json;

namespace LootPose.Companion.Tests;

public class RecoveryTests
{
    private static readonly DateTimeOffset Now = GameData.Instant("2026-11-21T20:00:00Z");
    private static readonly GameData Schedule = GameData.Parse("""
        {"weeklyReset":{"day":"sunday","timeUtc":"14:00"},"eventEnd":"2026-12-05T19:00:00Z","treasureHours":[]}
        """);

    [Theory]
    [InlineData("credentials")]
    [InlineData("link")]
    [InlineData("record")]
    [InlineData("unreadable")]
    [InlineData("push")]
    [InlineData("revision")]
    [InlineData("reset")]
    [InlineData("queue")]
    [InlineData("drop")]
    [InlineData("forget")]
    public void A_failed_save_keeps_the_last_durable_state(string operation)
    {
        using var folder = new TempFolder();
        var ledger = operation is "credentials" or "link" ? Ledger.Open(folder.Path) : TestLedgers.Linked(folder);
        if (operation == "link") ledger.LinkCredentials();
        if (operation is "push" or "reset" or "queue" or "drop")
        {
            ledger.Record(TestLedgers.Counted("a", "2026-11-15|normal", 4), "a|1|1");
            if (operation == "queue") ledger.Pushed(ledger.Data.Pending!, 1, 0);
        }
        ledger.Save();
        var before = JsonSerializer.Serialize(ledger.Data);
        Action change = operation switch
        {
            "credentials" => () => ledger.LinkCredentials(),
            "link" => () => ledger.CompleteLink(Now, 0, 0),
            "record" => () => ledger.Record(TestLedgers.Counted("b", "2026-11-15|normal", 2), "b|1|1"),
            "unreadable" => () => ledger.RecordUnreadable("b|1|1", Now, "1.0.0", Now, false),
            "push" => () => ledger.Pushed(ledger.Data.Pending!, 1, 0),
            "revision" => () => ledger.AdoptRevision(2),
            "reset" => () => ledger.Reset(1, 3),
            "queue" => () => ledger.QueuePushIfDifferent(new Dictionary<string, int>()),
            "drop" => () => ledger.DropPending(),
            "forget" => () => ledger.Forget(),
            _ => throw new ArgumentException(operation),
        };

        using (var locked = new FileStream(Path.Combine(folder.Path, "ledger.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(change);
            Assert.Equal(before, JsonSerializer.Serialize(ledger.Data));
        }
        Assert.Equal(before, JsonSerializer.Serialize(Ledger.Open(folder.Path).Data));
    }

    [Fact]
    public async Task A_failed_record_is_never_sent_and_is_retried_after_storage_recovers()
    {
        using var folder = new TempFolder();
        var ledger = TestLedgers.Linked(folder);
        var files = new RecoveringFiles();
        files.Add("a");
        var sync = new FakeSync(ledger);
        var engine = Engine(ledger, files, sync);
        using (var locked = new FileStream(Path.Combine(folder.Path, "ledger.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => engine.TickAsync(CancellationToken.None));
            await Assert.ThrowsAnyAsync<IOException>(() => engine.TickAsync(CancellationToken.None));
            Assert.Equal(0, sync.Calls);
            Assert.Null(ledger.Data.Pending);
        }
        await engine.TickAsync(CancellationToken.None);
        Assert.Equal(1, sync.Calls);
        Assert.Equal(3, Ledger.Open(folder.Path).Buckets()["2026-11-15|normal"]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Temporary_replay_access_failure_retries_and_does_not_block_other_files(bool duringProbe, bool denied)
    {
        using var folder = new TempFolder();
        var ledger = TestLedgers.Linked(folder);
        var files = new RecoveringFiles { DuringProbe = duringProbe, Failure = denied ? new UnauthorizedAccessException() : new IOException() };
        var blocked = files.Add("a");
        files.Add("b");
        var engine = Engine(ledger, files, new FakeSync(ledger));

        await engine.TickAsync(CancellationToken.None);
        Assert.False(ledger.Data.Files.ContainsKey(blocked.Key));
        Assert.Single(ledger.Data.Matches);
        Assert.Equal(3, ledger.Buckets()["2026-11-15|normal"]);

        files.Failure = null;
        await engine.TickAsync(CancellationToken.None);
        Assert.Equal(2, ledger.Data.Matches.Count);
        Assert.Equal(6, ledger.Buckets()["2026-11-15|normal"]);
    }

    [Fact]
    public void A_locked_finished_header_is_an_access_failure_rather_than_a_missing_key()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "header.replay");
        File.WriteAllBytes(path, ReplayBytes.Header(false, true, true));
        var demos = new DemosFolder(folder.Path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => demos.KeyMissing(path));
        }
        Assert.False(demos.KeyMissing(path));
    }

    [Fact]
    public async Task An_already_linked_PC_keeps_its_link_time_and_pending_counts()
    {
        using var folder = new TempFolder();
        var ledger = TestLedgers.Linked(folder);
        ledger.Record(TestLedgers.Counted("a", "2026-11-15|normal", 4), "a|1|1");
        var before = JsonSerializer.Serialize(ledger.Data);
        var http = new FakeHttp().Reply("""{"ok":true,"buckets":{},"revision":0,"epoch":0}""");
        var api = new DeviceApi(new HttpClient(http), new ServerConfig(new Uri("https://project.example/"), "test-key"));

        var result = await new Linker(api, ledger, () => Now, (_, _) => Task.CompletedTask).WaitAsync(CancellationToken.None);

        Assert.Equal(LinkResult.Linked, result);
        Assert.Equal(before, JsonSerializer.Serialize(ledger.Data));
        Assert.Empty(http.Requests);
    }

    private static CompanionEngine Engine(Ledger ledger, IReplayFiles files, ISyncClient sync) =>
        new(ledger, files, new FakeData(Schedule), sync, () => Now, () => false, "1.0.0");

    private sealed class RecoveringFiles : IReplayFiles
    {
        private readonly List<ReplayFile> _files = new();
        public bool DuringProbe { get; init; }
        public Exception? Failure { get; set; }
        public ReplayFile Add(string name)
        {
            var file = new ReplayFile(name, name + ".replay", 1000, Now.AddHours(-1).UtcDateTime, Now.AddMinutes(-30).UtcDateTime);
            _files.Add(file);
            return file;
        }
        public IReadOnlyList<ReplayFile> List() => _files;
        public bool KeyMissing(string path)
        {
            if (path == "a" && DuringProbe && Failure is { } failure) throw failure;
            return false;
        }
        public ReplayFacts Read(string path)
        {
            if (path == "a" && !DuringProbe && Failure is { } failure) throw failure;
            return new ReplayFacts(path, Now.AddHours(-1), "Playlist_DefaultSquad", true,
                new[] { new ChestFact(10, false), new ChestFact(20, false), new ChestFact(30, false) });
        }
    }
}
