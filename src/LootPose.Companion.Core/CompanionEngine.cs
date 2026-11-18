namespace LootPose.Companion;

/// <summary>What the window and the tray icon show.</summary>
public sealed record EngineView(string Status, bool Linked, string? Code, bool ShowReplayHint, IReadOnlyList<MatchEntry> RecentMatches);

/// <summary>
/// One look at the Demos folder: read each finished replay once, record it in the ledger, and send the row. The app
/// calls <see cref="TickAsync"/> on every change in the folder and every 30 seconds. One look runs at a time, and the
/// ledger is only changed inside a look or inside <see cref="ExclusiveAsync{T}"/>.
/// </summary>
public sealed class CompanionEngine
{
    private readonly Ledger _ledger;
    private readonly IReplayFiles _files;
    private readonly IGameDataSource _data;
    private readonly ISyncClient? _sync;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<bool> _fortniteRunning;
    private readonly string _appVersion;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Backoff _backoff = new();
    private readonly ReplayHint _hint = new();
    private readonly Dictionary<string, (long Length, DateTime LastWriteUtc)> _lastLook = new(StringComparer.OrdinalIgnoreCase);
    private SyncState _syncState = SyncState.Synced;
    private bool _eventEnded;
    private DateTimeOffset _nextStatusAt;

    public CompanionEngine(Ledger ledger, IReplayFiles files, IGameDataSource data, ISyncClient? sync,
        Func<DateTimeOffset> clock, Func<bool> fortniteRunning, string appVersion)
    {
        _ledger = ledger;
        _files = files;
        _data = data;
        _sync = sync;
        _clock = clock;
        _fortniteRunning = fortniteRunning;
        _appVersion = appVersion;
        View = new EngineView(StatusText.NotLinked, false, null, false, Array.Empty<MatchEntry>());
    }

    /// <summary>Raised after every look, and when a replay starts being read. Not on the UI thread.</summary>
    public event Action<EngineView>? ViewChanged;

    public EngineView View { get; private set; }

    /// <summary>True when a finished replay is settling: look again in a few seconds instead of in 30.</summary>
    public bool LookSoon { get; private set; }

    public async Task TickAsync(CancellationToken cancel)
    {
        if (!await _gate.WaitAsync(0, cancel).ConfigureAwait(false)) return; // a look is already running
        try
        {
            LookSoon = false;
            var flags = new LookFlags();
            var files = _files.List();
            _hint.Observe(_clock().UtcDateTime, _fortniteRunning(), files.Count == 0 ? (DateTime?)null : files.Max(f => f.LastWriteUtc));
            if (_ledger.IsLinked) await SyncAsync(cancel).ConfigureAwait(false);
            if (_ledger.IsLinked)
            {
                var data = await _data.GetAsync(cancel).ConfigureAwait(false);
                _eventEnded = data is not null && Bucketing.EventEnded(data, _clock());
                await HandleFilesAsync(files, data, flags, cancel).ConfigureAwait(false);
                await SyncAsync(cancel).ConfigureAwait(false);
            }
            Publish(flags);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs <paramref name="work"/> while no look is running: linking and unlinking change the ledger too.</summary>
    public async Task<T> ExclusiveAsync<T>(Func<Task<T>> work, CancellationToken cancel)
    {
        await _gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task HandleFilesAsync(IReadOnlyList<ReplayFile> files, GameData? data, LookFlags flags, CancellationToken cancel)
    {
        var linkedAt = _ledger.Data.LinkedAt!.Value;
        foreach (var file in files)
        {
            if (!NeedsLook(file)) continue;

            // A replay last written before the link holds a match from before it, which never counts: it is not read.
            if (file.LastWriteUtc < linkedAt.UtcDateTime)
            {
                _ledger.Record(new MatchEntry
                {
                    Key = "file:" + file.Key,
                    StartUtc = Utc(file.CreatedUtc),
                    Outcome = MatchOutcome.PlayedBeforeLinking,
                    HandledAt = _clock(),
                }, file.Key);
                continue;
            }

            var sameAsLastLook = _lastLook.TryGetValue(file.Path, out var last) && last == (file.Length, file.LastWriteUtc);
            _lastLook[file.Path] = (file.Length, file.LastWriteUtc);
            bool keyMissing;
            try
            {
                keyMissing = _files.KeyMissing(file.Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LookSoon = true;
                continue;
            }
            switch (ReplayReadiness.Check(keyMissing, file.LastWriteUtc, sameAsLastLook, _clock().UtcDateTime))
            {
                case Readiness.Recording:
                    flags.InProgress = true;
                    continue;
                case Readiness.Settling:
                    LookSoon = true;
                    continue;
                case Readiness.Abandoned:
                    _ledger.RecordUnreadable(file.Key, Utc(file.CreatedUtc), _appVersion, _clock(), cutShort: true);
                    continue;
            }

            if (data is null)
            {
                // A match is not counted until a copy of LootPose's data file exists. It waits here.
                flags.WaitingForData = true;
                return;
            }

            ReplayFacts facts;
            flags.Counting = true;
            Publish(flags);
            try
            {
                facts = await Task.Run(() => _files.Read(file.Path), cancel).ConfigureAwait(false);
            }
            catch (ReplayBusyException)
            {
                LookSoon = true;
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LookSoon = true;
                continue;
            }
            catch (Exception e) when (e is InvalidDataException or TimeoutException)
            {
                _ledger.RecordUnreadable(file.Key, Utc(file.CreatedUtc), _appVersion, _clock(), cutShort: false);
                continue;
            }
            finally
            {
                flags.Counting = false;
            }

            var outcome = MatchRules.Decide(facts, linkedAt, data);
            var buckets = outcome == MatchOutcome.Counted ? MatchRules.Buckets(facts, data) : new Dictionary<string, int>(StringComparer.Ordinal);
            _ledger.Record(new MatchEntry
            {
                Key = string.IsNullOrEmpty(facts.SessionId) ? "file:" + file.Key : facts.SessionId,
                StartUtc = facts.MatchStartUtc ?? Utc(file.CreatedUtc),
                Playlist = facts.Playlist,
                Outcome = outcome,
                Chests = buckets.Values.Sum(),
                Buckets = buckets,
                HandledAt = _clock(),
            }, file.Key);
        }
    }

    /// <summary>A file not handled yet, or one that an older version of the companion could not read.</summary>
    private bool NeedsLook(ReplayFile file)
    {
        if (!_ledger.Data.Files.TryGetValue(file.Key, out var handled)) return true;
        return handled.StartsWith(Ledger.UnreadablePrefix, StringComparison.Ordinal) && handled != Ledger.UnreadablePrefix + _appVersion;
    }

    private async Task SyncAsync(CancellationToken cancel)
    {
        if (_sync is null) return;
        var now = _clock();
        if (!_backoff.Allows(now)) return;
        if (_ledger.Data.Pending is null && now < _nextStatusAt) return;
        _nextStatusAt = now.AddSeconds(30);
        var result = await _sync.SyncAsync(cancel).ConfigureAwait(false);
        _syncState = result.State;
        if (result.State is SyncState.Offline or SyncState.RateLimited) _backoff.Failed(now, result.Wait);
        else _backoff.Succeeded();
    }

    private void Publish(LookFlags flags)
    {
        var linked = _ledger.IsLinked;
        string? code = null;
        if (!linked)
        {
            // Made on first run, so the window shows the code at once.
            var (_, secret) = _ledger.LinkCredentials();
            code = LinkCodes.Code(LinkCodes.Fingerprint(secret));
        }
        View = new EngineView(StatusFor(flags, linked), linked, code, linked && _hint.Show, Recent());
        ViewChanged?.Invoke(View);
    }

    private string StatusFor(LookFlags flags, bool linked)
    {
        if (!linked) return StatusText.NotLinked;
        if (flags.Counting) return StatusText.Counting;
        if (flags.InProgress) return StatusText.InProgress;
        var latest = _ledger.Data.Matches.OrderByDescending(m => m.HandledAt).FirstOrDefault();
        if (latest is { Outcome: MatchOutcome.CouldNotBeRead, CutShort: false }) return StatusText.Unsupported;
        if (_eventEnded) return StatusText.EventEnded;
        if (flags.WaitingForData) return StatusText.WaitingForData;
        var counted = _ledger.Data.Matches
            .Where(m => m.Outcome == MatchOutcome.Counted && m.DeviceId == _ledger.Data.DeviceId)
            .OrderByDescending(m => m.HandledAt)
            .FirstOrDefault();
        if (counted is null) return StatusText.Waiting;
        var synced = _ledger.Data.Pending is null && _syncState != SyncState.Rejected;
        return StatusText.LastMatch(counted.Chests, synced);
    }

    /// <summary>The window lists the last 20 matches, newest first.</summary>
    private IReadOnlyList<MatchEntry> Recent() =>
        _ledger.Data.Matches.OrderByDescending(m => m.StartUtc ?? m.HandledAt).Take(20).ToList();

    private static DateTimeOffset Utc(DateTime time) => new(DateTime.SpecifyKind(time, DateTimeKind.Utc));

    private sealed class LookFlags
    {
        public bool Counting;
        public bool InProgress;
        public bool WaitingForData;
    }
}
