namespace LootPose.Companion.Tests;

/// <summary>A Demos folder in memory. A file added with no contents throws when read, as an unreadable replay does.</summary>
internal sealed class FakeReplayFiles : IReplayFiles
{
    public List<ReplayFile> Files { get; } = new();

    public Dictionary<string, Func<ReplayFacts>> Contents { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths whose key is not written yet.</summary>
    public HashSet<string> Recording { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Reads { get; } = new();

    public ReplayFile Add(string name, DateTime createdUtc, DateTime writtenUtc, Func<ReplayFacts>? read = null)
    {
        var file = new ReplayFile(@"C:\Demos\" + name, name, 1000, createdUtc, writtenUtc);
        Files.Add(file);
        if (read is not null) Contents[file.Path] = read;
        return file;
    }

    public IReadOnlyList<ReplayFile> List() => Files.OrderBy(f => f.LastWriteUtc).ToList();

    public bool KeyMissing(string path) => Recording.Contains(path);

    public ReplayFacts Read(string path)
    {
        Reads.Add(path);
        return Contents.TryGetValue(path, out var read) ? read() : throw new InvalidDataException("The replay could not be read.");
    }
}

internal sealed class FakeData(GameData? value) : IGameDataSource
{
    public GameData? Value { get; set; } = value;

    public Task<GameData?> GetAsync(CancellationToken cancel) => Task.FromResult(Value);
}

/// <summary>Accepts every push at once, unless <see cref="Next"/> says otherwise.</summary>
internal sealed class FakeSync(Ledger ledger) : ISyncClient
{
    public SyncResult Next { get; set; } = new(SyncState.Synced);

    public int Calls { get; private set; }

    public Task<SyncResult> SyncAsync(CancellationToken cancel)
    {
        if (ledger.Data.Pending is null) return Task.FromResult(Next);
        Calls++;
        if (Next.State == SyncState.Synced && ledger.Data.Pending is { } pending)
        {
            ledger.Pushed(pending, ledger.Data.Revision + 1, ledger.Data.Epoch);
        }
        return Task.FromResult(Next);
    }
}
