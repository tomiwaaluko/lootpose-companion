using System.Diagnostics;

namespace LootPose.Companion.App;

/// <summary>The tray icon, the window, and what drives the engine.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly HttpClient _http = new();
    private readonly CancellationTokenSource _quit = new();
    private readonly Ledger _ledger;
    private readonly DeviceApi? _api;
    private readonly CompanionEngine _engine;
    private readonly ReplayWatcher _watcher;
    private readonly MainWindow _window;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _soon = new() { Interval = 6000 };
    private readonly RegisteredWaitHandle _showRegistration;
    private CancellationTokenSource? _linking;

    public TrayApp(bool showWindow, EventWaitHandle showSignal)
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"LootPoseCompanion/{CompanionInfo.VersionText}");
        _ledger = Ledger.Open(CompanionInfo.DataFolder);
        var server = ServerConfig.Find(typeof(TrayApp).Assembly);
        _api = server is null ? null : new DeviceApi(_http, server);
        var demos = new DemosFolder(DemosFolder.Default);
        _engine = new CompanionEngine(
            _ledger,
            demos,
            new GameDataStore(_http, CompanionInfo.DataFolder, () => DateTimeOffset.UtcNow),
            _api is null ? null : new SyncClient(_api, _ledger),
            () => DateTimeOffset.UtcNow,
            FortniteProcess.IsRunning,
            CompanionInfo.VersionText);

        _window = new MainWindow();
        _ = _window.Handle; // made now, so views from other threads can be shown with BeginInvoke
        _window.LinkClicked += OnLinkClicked;
        _window.UnlinkClicked += OnUnlinkClicked;

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = CompanionInfo.ProductName,
            Visible = true,
            ContextMenuStrip = TrayMenu(),
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

        _engine.ViewChanged += view => OnUi(() => Apply(view));
        _soon.Tick += (_, _) =>
        {
            _soon.Stop();
            Kick();
        };
        _watcher = new ReplayWatcher(demos.Folder);
        _watcher.Changed += Kick;
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => OnUi(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        var startWithWindows = new StartWithWindows();
        if (Environment.ProcessPath is { } exe)
        {
            startWithWindows.Refresh(exe);
            _window.ShowStartWithWindows(startWithWindows.IsOn());
            _window.StartWithWindowsChanged += on =>
            {
                if (!startWithWindows.Set(on, exe)) _window.ShowStartWithWindows(startWithWindows.IsOn());
            };
        }

        var updates = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromDays(1).TotalMilliseconds };
        updates.Tick += (_, _) => CheckForUpdate();
        updates.Start();
        CheckForUpdate();

        if (showWindow) ShowWindow();
        Kick();
    }

    private ContextMenuStrip TrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Open LootPose", null, (_, _) => Browser.Open(CompanionInfo.WebAppUrl));
        menu.Items.Add("Quit", null, (_, _) => Quit());
        return menu;
    }

    /// <summary>At start and once a day. Only shows Update available; never downloads anything.</summary>
    private async void CheckForUpdate()
    {
        try
        {
            if (await new UpdateCheck(_http).NewerExistsAsync(CompanionInfo.Version, _quit.Token)) _window.ShowUpdateAvailable();
        }
        catch (OperationCanceledException)
        {
            // Quitting.
        }
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Normal;
        _window.Activate();
    }

    private void Apply(EngineView view)
    {
        _window.ShowView(view);
        // Windows Forms has long allowed 63 characters in a tray tooltip (Decision 14).
        _tray.Text = view.Status.Length <= 63 ? view.Status : view.Status[..63];
        if (_engine.LookSoon && !_soon.Enabled) _soon.Start();
    }

    private void OnUi(Action action)
    {
        try
        {
            if (!_window.IsDisposed) _window.BeginInvoke(action);
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
        {
            // Quitting.
        }
    }

    /// <summary>Starts a look, unless one is running. Called on every change in the Demos folder and every 30 seconds.</summary>
    private void Kick() => _ = Task.Run(async () =>
    {
        try
        {
            await _engine.TickAsync(_quit.Token);
        }
        catch (OperationCanceledException)
        {
            // Quitting.
        }
        catch (Exception e)
        {
            // A look that failed (a locked file, a full disk) is tried again at the next tick.
            Debug.WriteLine(e);
        }
    });

    private async void OnLinkClicked()
    {
        if (_api is not { } api)
        {
            MessageBox.Show(
                "This copy of the companion was built without LootPose's server address, so it cannot link. Download it again from the latest release.",
                CompanionInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_linking is not null) return;
        var linking = CancellationTokenSource.CreateLinkedTokenSource(_quit.Token);
        _linking = linking;
        _window.SetLinking(true);
        try
        {
            await _engine.ExclusiveAsync(async () =>
            {
                var (deviceId, secret) = _ledger.LinkCredentials();
                Browser.Open(LinkCodes.LinkAddress(deviceId, LinkCodes.Fingerprint(secret)).AbsoluteUri);
                return await new Linker(api, _ledger, () => DateTimeOffset.UtcNow, Task.Delay).WaitAsync(linking.Token);
            }, linking.Token);
        }
        catch (OperationCanceledException)
        {
            // Quitting.
        }
        finally
        {
            _linking = null;
            linking.Dispose();
            if (!_window.IsDisposed) _window.SetLinking(false);
        }
        Kick();
    }

    /// <summary>
    /// The companion cannot unlink itself on the server (Decision 3): it forgets the link on this PC and opens LootPose,
    /// where the player presses Unlink next to "PC companion".
    /// </summary>
    private async void OnUnlinkClicked()
    {
        var answer = MessageBox.Show(
            "Unlink this PC? The chests it counted stay in your account.\n\nLootPose will open so you can also press Unlink next to \"PC companion\" there.",
            CompanionInfo.ProductName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        try
        {
            await _engine.ExclusiveAsync(() =>
            {
                _ledger.Forget();
                return Task.FromResult(true);
            }, _quit.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        Browser.Open(CompanionInfo.WebAppUrl);
        Kick();
    }

    private void Quit()
    {
        _quit.Cancel();
        _showRegistration.Unregister(null);
        _watcher.Dispose();
        _soon.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _window.Dispose();
        _http.Dispose();
        ExitThread();
    }
}
