namespace LootPose.Companion;

/// <summary>Where the engine gets LootPose's data file.</summary>
public interface IGameDataSource
{
    /// <summary>The newest copy of the data file, or null when there has never been one.</summary>
    Task<GameData?> GetAsync(CancellationToken cancel);
}

/// <summary>
/// Fetches the data file when the app starts and again when the copy is more than an hour old, and keeps the last
/// copy in the data folder as treasures.json. A failed fetch is tried again 5 minutes later.
/// </summary>
public sealed class GameDataStore(HttpClient http, string folder, Func<DateTimeOffset> clock, string address = CompanionInfo.DataFileUrl)
    : IGameDataSource
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);

    private readonly string _path = Path.Combine(folder, "treasures.json");
    private GameData? _data;
    private bool _diskRead;
    private DateTimeOffset? _fetchedAt;
    private DateTimeOffset? _failedAt;

    public async Task<GameData?> GetAsync(CancellationToken cancel)
    {
        var now = clock();
        if (!_diskRead)
        {
            // The copy on disk is used until the first fetch of this run succeeds.
            _diskRead = true;
            _data = ReadDisk();
        }
        var fresh = _fetchedAt is { } fetched && now - fetched < MaxAge;
        var resting = _failedAt is { } failed && now - failed < RetryAfterFailure;
        if (fresh || resting) return _data;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var json = await http.GetStringAsync(new Uri(address), timeout.Token).ConfigureAwait(false);
            var data = GameData.Parse(json);
            AtomicFile.WriteAllText(_path, json);
            _data = data;
            _fetchedAt = now;
            _failedAt = null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FormatException or IOException)
        {
            if (cancel.IsCancellationRequested) throw;
            _failedAt = now;
        }
        return _data;
    }

    private GameData? ReadDisk()
    {
        try
        {
            return File.Exists(_path) ? GameData.Parse(File.ReadAllText(_path)) : null;
        }
        catch (Exception e) when (e is FormatException or IOException)
        {
            return null;
        }
    }
}
