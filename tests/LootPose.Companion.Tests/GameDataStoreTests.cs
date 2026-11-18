namespace LootPose.Companion.Tests;

public class GameDataStoreTests
{
    private const string Json = """{"weeklyReset":{"day":"sunday","timeUtc":"14:00"},"treasureHours":[]}""";

    [Fact]
    public async Task Fetches_at_start_and_again_only_when_the_copy_is_an_hour_old()
    {
        using var folder = new TempFolder();
        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var http = new FakeHttp().Reply(Json).Reply(Json);
        var store = new GameDataStore(new HttpClient(http), folder.Path, () => now);

        Assert.NotNull(await store.GetAsync(CancellationToken.None));
        now = now.AddMinutes(59);
        Assert.NotNull(await store.GetAsync(CancellationToken.None));
        Assert.Single(http.Requests);

        now = now.AddMinutes(2);
        Assert.NotNull(await store.GetAsync(CancellationToken.None));
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal("https://lootpose.vercel.app/data/treasures.json", http.Requests[0].Uri!.ToString());
        Assert.Equal(Json, File.ReadAllText(Path.Combine(folder.Path, "treasures.json")));
    }

    [Fact]
    public async Task Offline_it_uses_the_copy_kept_on_disk()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "treasures.json"), Json);
        var store = new GameDataStore(new HttpClient(new FakeHttp().Fail()), folder.Path, () => DateTimeOffset.UtcNow);

        var data = await store.GetAsync(CancellationToken.None);
        Assert.NotNull(data);
        Assert.Equal(DayOfWeek.Sunday, data!.WeeklyReset.Day);
    }

    [Fact]
    public async Task With_no_copy_and_no_connection_there_is_no_data_and_it_tries_again_five_minutes_later()
    {
        using var folder = new TempFolder();
        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var http = new FakeHttp().Fail().Reply(Json);
        var store = new GameDataStore(new HttpClient(http), folder.Path, () => now);

        Assert.Null(await store.GetAsync(CancellationToken.None));
        now = now.AddMinutes(4);
        Assert.Null(await store.GetAsync(CancellationToken.None));
        Assert.Single(http.Requests);

        now = now.AddMinutes(2);
        Assert.NotNull(await store.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_broken_download_does_not_replace_a_good_copy()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "treasures.json"), Json);
        var store = new GameDataStore(new HttpClient(new FakeHttp().Reply("<html>")), folder.Path, () => DateTimeOffset.UtcNow);

        Assert.NotNull(await store.GetAsync(CancellationToken.None));
        Assert.Equal(Json, File.ReadAllText(Path.Combine(folder.Path, "treasures.json")));
    }
}
