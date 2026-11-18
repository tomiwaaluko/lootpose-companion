namespace LootPose.Companion;

/// <summary>
/// Tells the engine to look at the Demos folder: on every change in it, and every 30 seconds in case a change was
/// missed. The folder only exists after Fortnite saves its first replay, so watching starts whenever it appears.
/// </summary>
public sealed class ReplayWatcher : IDisposable
{
    public static readonly TimeSpan Fallback = TimeSpan.FromSeconds(30);

    private readonly string _folder;
    private readonly object _lock = new();
    private readonly System.Threading.Timer _timer;
    private FileSystemWatcher? _watcher;

    public ReplayWatcher(string folder)
    {
        _folder = folder;
        TryWatch();
        _timer = new System.Threading.Timer(_ => Tick(), null, Fallback, Fallback);
    }

    /// <summary>Raised on a thread-pool thread.</summary>
    public event Action? Changed;

    public void Dispose()
    {
        _timer.Dispose();
        lock (_lock)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void Tick()
    {
        TryWatch();
        Changed?.Invoke();
    }

    private void TryWatch()
    {
        lock (_lock)
        {
            if (_watcher is not null || !Directory.Exists(_folder)) return;
            try
            {
                var watcher = new FileSystemWatcher(_folder, "*.replay")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Created += (_, _) => Changed?.Invoke();
                watcher.Changed += (_, _) => Changed?.Invoke();
                watcher.Renamed += (_, _) => Changed?.Invoke();
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch (Exception e) when (e is ArgumentException or IOException)
            {
                // The folder went away again: the next tick tries again.
            }
        }
    }
}
