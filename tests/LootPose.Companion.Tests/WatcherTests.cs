namespace LootPose.Companion.Tests;

public class WatcherTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 20, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, 30, false, Readiness.Recording)]
    [InlineData(true, 601, true, Readiness.Abandoned)]
    [InlineData(false, 2, true, Readiness.Settling)]
    [InlineData(false, 5, false, Readiness.Settling)]
    [InlineData(false, 5, true, Readiness.Ready)]
    [InlineData(false, 61, false, Readiness.Ready)]
    public void A_replay_is_ready_once_its_key_is_written_and_it_has_been_still_for_five_seconds(
        bool keyMissing, int secondsQuiet, bool sameAsLastLook, Readiness expected)
    {
        Assert.Equal(expected, ReplayReadiness.Check(keyMissing, Now.AddSeconds(-secondsQuiet), sameAsLastLook, Now));
    }

    [Fact]
    public void The_hint_needs_fifteen_minutes_of_Fortnite_with_no_new_replay()
    {
        var hint = new ReplayHint();
        var yesterday = Now.AddDays(-1);

        hint.Observe(Now, fortniteRunning: true, newestReplayWriteUtc: yesterday);
        Assert.False(hint.Show);
        hint.Observe(Now.AddMinutes(14), true, yesterday);
        Assert.False(hint.Show);
        hint.Observe(Now.AddMinutes(15), true, yesterday);
        Assert.True(hint.Show);

        hint.Observe(Now.AddMinutes(16), true, Now.AddMinutes(16));
        Assert.False(hint.Show);
        hint.Observe(Now.AddMinutes(40), false, Now.AddMinutes(16));
        Assert.False(hint.Show);
    }

    [Fact]
    public void The_Demos_folder_lists_replays_oldest_first_with_LED_Ns_file_keys()
    {
        using var folder = new TempFolder();
        var older = Path.Combine(folder.Path, "UnsavedReplay-2026.11.21-19.00.00.replay");
        var newer = Path.Combine(folder.Path, "UnsavedReplay-2026.11.21-20.00.00.replay");
        File.WriteAllBytes(newer, ReplayBytes.Header(live: false, encrypted: true, keyWritten: true));
        File.WriteAllBytes(older, ReplayBytes.Header(live: true, encrypted: true, keyWritten: false));
        File.WriteAllText(Path.Combine(folder.Path, "notes.txt"), "not a replay");
        File.SetLastWriteTimeUtc(older, Now.AddHours(-1));
        File.SetLastWriteTimeUtc(newer, Now);

        var demos = new DemosFolder(folder.Path);
        var files = demos.List();

        Assert.Equal(new[] { older, newer }, files.Select(f => f.Path));
        Assert.Equal($"UnsavedReplay-2026.11.21-20.00.00.replay|{files[1].Length}|{Now.Ticks}", files[1].Key);
        Assert.True(demos.KeyMissing(older));
        Assert.False(demos.KeyMissing(newer));
        Assert.Empty(new DemosFolder(Path.Combine(folder.Path, "missing")).List());
    }

    [Fact]
    public void The_default_folder_is_Fortnites_Demos_folder()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal(Path.Combine(local, "FortniteGame", "Saved", "Demos"), DemosFolder.Default);
    }

    [Fact]
    public void Fortnite_is_found_by_its_process_name_only()
    {
        Assert.Equal("FortniteClient-Win64-Shipping", FortniteProcess.Name);
        _ = FortniteProcess.IsRunning(); // must not throw, whether Fortnite is running or not
    }
}
