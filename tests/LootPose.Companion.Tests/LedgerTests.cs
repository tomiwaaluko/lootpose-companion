namespace LootPose.Companion.Tests;

public class LedgerTests
{
    [Fact]
    public void The_secret_is_kept_encrypted_and_survives_a_restart()
    {
        using var folder = new TempFolder();
        var ledger = TestLedgers.Linked(folder);
        var secret = ledger.SecretHex();
        Assert.Matches("^[0-9a-f]{64}$", secret);

        var reopened = Ledger.Open(folder.Path);
        Assert.True(reopened.IsLinked);
        Assert.Equal(ledger.Data.DeviceId, reopened.Data.DeviceId);
        Assert.Equal(secret, reopened.SecretHex());
        Assert.DoesNotContain(secret, File.ReadAllText(Path.Combine(folder.Path, "ledger.json")));
    }

    [Fact]
    public void The_device_id_and_secret_stay_the_same_until_a_link_is_lost()
    {
        using var folder = new TempFolder();
        var ledger = Ledger.Open(folder.Path);
        var first = ledger.LinkCredentials();
        Assert.Equal(32, first.Secret.Length);
        Assert.True(Guid.TryParse(first.DeviceId, out _));
        Assert.Equal(first.DeviceId, ledger.LinkCredentials().DeviceId);
        Assert.False(ledger.IsLinked);

        ledger.CompleteLink(TestLedgers.LinkedAt, 0, 0);
        Assert.True(ledger.IsLinked);
        ledger.Forget();
        Assert.False(ledger.IsLinked);
        Assert.NotEqual(first.DeviceId, ledger.LinkCredentials().DeviceId);
    }

    [Fact]
    public void A_match_counts_once()
    {
        using var folder = new TempFolder();
        var ledger = TestLedgers.Linked(folder);

        Assert.True(ledger.Record(TestLedgers.Counted("s1", "2026-11-15|normal", 4), "a.replay|1|1"));
        Assert.False(ledger.Record(TestLedgers.Counted("s1", "2026-11-15|normal", 4), "renamed.replay|1|2"));

        Assert.Equal(new Dictionary<string, int> { ["2026-11-15|normal"] = 4 }, ledger.Buckets());
        Assert.Equal("s1", ledger.Data.Files["renamed.replay|1|2"]);
        Assert.Single(ledger.Data.Matches);
    }

    [Fact]
    public void A_new_link_starts_from_zero_and_never_counts_an_old_match_again()
    {
        using var folder = new TempFolder();
        var ledger = TestLedgers.Linked(folder);
        ledger.Record(TestLedgers.Counted("s1", "2026-11-15|normal", 4), "a|1|1");

        ledger.Forget();
        Assert.Empty(ledger.Buckets());
        ledger.LinkCredentials();
        ledger.CompleteLink(GameData.Instant("2026-11-22T00:00:00Z"), 0, 3);

        Assert.False(ledger.Record(TestLedgers.Counted("s1", "2026-11-15|normal", 4), "a|1|1"));
        Assert.True(ledger.Record(TestLedgers.Counted("s2", "2026-11-22|normal", 2), "b|1|1"));
        Assert.Equal(new Dictionary<string, int> { ["2026-11-22|normal"] = 2 }, ledger.Buckets());
        Assert.Equal(2, ledger.Data.Matches.Count);
    }

    [Fact]
    public void Each_counted_match_saves_a_push_of_the_whole_row_before_it_is_sent()
    {
        using var folder = new TempFolder();
        var ledger = TestLedgers.Linked(folder);
        ledger.Record(TestLedgers.Counted("s1", "2026-11-15|normal", 4), "a|1|1");
        var first = ledger.Data.Pending!;
        ledger.Record(TestLedgers.Counted("s2", "2026-11-15|normal", 1), "b|1|1");

        var saved = Ledger.Open(folder.Path).Data.Pending!;
        Assert.NotEqual(first.RequestId, saved.RequestId);
        Assert.Equal(new Dictionary<string, int> { ["2026-11-15|normal"] = 5 }, saved.Buckets);
    }

    [Fact]
    public void Only_the_push_that_was_sent_is_cleared()
    {
        using var folder = new TempFolder();
        var ledger = TestLedgers.Linked(folder);
        ledger.Record(TestLedgers.Counted("s1", "2026-11-15|normal", 4), "a|1|1");
        var sent = ledger.Data.Pending!;
        ledger.Record(TestLedgers.Counted("s2", "2026-11-15|normal", 1), "b|1|1");

        ledger.Pushed(sent, revision: 1, epoch: 0);
        Assert.NotNull(ledger.Data.Pending);
        Assert.Equal(1, ledger.Data.Revision);

        ledger.Pushed(ledger.Data.Pending!, revision: 2, epoch: 0);
        Assert.Null(ledger.Data.Pending);
        Assert.Equal(2, ledger.Data.Revision);
    }

    [Fact]
    public void A_reset_marks_the_counted_matches_and_empties_the_row()
    {
        using var folder = new TempFolder();
        var ledger = TestLedgers.Linked(folder);
        ledger.Record(TestLedgers.Counted("s1", "2026-11-15|normal", 4), "a|1|1");

        ledger.Reset(epoch: 1, revision: 7);

        Assert.Equal(MatchOutcome.BeforeReset, ledger.Data.Matches.Single().Outcome);
        Assert.Empty(ledger.Buckets());
        Assert.Null(ledger.Data.Pending);
        Assert.Equal((1, 7), (ledger.Data.Epoch, ledger.Data.Revision));
    }

    [Fact]
    public void An_unreadable_replay_is_replaced_when_a_later_version_reads_it()
    {
        using var folder = new TempFolder();
        var ledger = TestLedgers.Linked(folder);
        ledger.RecordUnreadable("a|1|1", null, "1.0.0", TestLedgers.LinkedAt.AddDays(1), cutShort: false);
        Assert.Equal("unreadable:1.0.0", ledger.Data.Files["a|1|1"]);
        Assert.Equal(MatchOutcome.CouldNotBeRead, ledger.Data.Matches.Single().Outcome);

        ledger.Record(TestLedgers.Counted("s1", "2026-11-15|normal", 2), "a|1|1");

        Assert.Equal(MatchOutcome.Counted, ledger.Data.Matches.Single().Outcome);
        Assert.Equal("s1", ledger.Data.Files["a|1|1"]);
    }

    [Fact]
    public void An_unreadable_ledger_is_kept_aside_and_the_PC_starts_unlinked()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "ledger.json"), "{ not json");

        var ledger = Ledger.Open(folder.Path);

        Assert.False(ledger.IsLinked);
        Assert.Single(Directory.GetFiles(folder.Path, "ledger.json.corrupt-*"));
    }

    [Fact]
    public void Saving_leaves_no_temporary_file()
    {
        using var folder = new TempFolder();
        TestLedgers.Linked(folder);
        Assert.Equal(new[] { "ledger.json" }, Directory.GetFiles(folder.Path).Select(Path.GetFileName));
    }
}
