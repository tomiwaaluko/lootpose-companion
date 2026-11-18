using System.Globalization;

namespace LootPose.Companion.App;

/// <summary>The one small window: the status line, linking, the last 20 matches and the controls.</summary>
internal sealed class MainWindow : Form
{
    private const string LinkText = "Link this PC";

    private readonly Label _status = new() { AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
    private readonly Label _hint = new() { AutoSize = true, ForeColor = Color.DarkGoldenrod, Text = ReplayHint.Text, Visible = false };
    private readonly Label _linkPrompt = new() { AutoSize = true, Text = StatusText.LinkPrompt };
    private readonly Label _code = new() { AutoSize = true, Font = new Font("Consolas", 16f, FontStyle.Bold) };
    private readonly Button _link = new() { AutoSize = true, Text = LinkText, Visible = false };
    private readonly Label _linked = new() { AutoSize = true, Text = StatusText.Linked, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
    private readonly Label _setup = new() { AutoSize = true, Text = StatusText.ReplaySetup };
    private readonly ListView _matches = new() { View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, Size = new Size(460, 260) };
    private readonly Button _open = new() { AutoSize = true, Text = "Open LootPose" };
    private readonly CheckBox _startWithWindows = new() { AutoSize = true, Text = "Start with Windows", Visible = false };
    private readonly Button _unlink = new() { AutoSize = true, Text = "Unlink this PC", Visible = false };
    private readonly LinkLabel _update = new() { AutoSize = true, Text = "Update available", Visible = false };

    public MainWindow()
    {
        Text = CompanionInfo.ProductName;
        Icon = SystemIcons.Application;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(12);

        _matches.Columns.Add("When", 160);
        _matches.Columns.Add("Mode", 160);
        _matches.Columns.Add("Result", 120);

        var controls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        controls.Controls.AddRange(new Control[] { _open, _startWithWindows, _unlink, _update });

        var layout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.AddRange(new Control[] { _status, _hint, _linkPrompt, _code, _link, _linked, _setup, _matches, controls });
        Controls.Add(layout);

        _link.Click += (_, _) => LinkClicked?.Invoke();
        _unlink.Click += (_, _) => UnlinkClicked?.Invoke();
        _open.Click += (_, _) => Browser.Open(CompanionInfo.WebAppUrl);
        _update.LinkClicked += (_, _) => Browser.Open(CompanionInfo.ReleasesPage);
        _startWithWindows.CheckedChanged += (_, _) => StartWithWindowsChanged?.Invoke(_startWithWindows.Checked);
    }

    public event Action? LinkClicked;

    public event Action? UnlinkClicked;

    public event Action<bool>? StartWithWindowsChanged;

    public void ShowView(EngineView view)
    {
        _status.Text = view.Status;
        _hint.Visible = view.ShowReplayHint;
        _linkPrompt.Visible = !view.Linked;
        _code.Visible = !view.Linked;
        _code.Text = view.Code ?? "";
        _link.Visible = !view.Linked;
        _linked.Visible = view.Linked;
        _setup.Visible = view.Linked;
        _unlink.Visible = view.Linked;

        _matches.BeginUpdate();
        _matches.Items.Clear();
        foreach (var entry in view.RecentMatches)
        {
            var row = MatchRows.For(entry, TimeZoneInfo.Local, CultureInfo.CurrentCulture);
            _matches.Items.Add(new ListViewItem(new[] { row.When, row.Mode, row.Result }));
        }
        _matches.EndUpdate();
    }

    public void SetLinking(bool linking)
    {
        _link.Enabled = !linking;
        _link.Text = linking ? "Waiting for LootPose…" : LinkText;
    }

    /// <summary>Task 12 wires this.</summary>
    public void ShowStartWithWindows(bool on)
    {
        _startWithWindows.Checked = on;
        _startWithWindows.Visible = true;
    }

    /// <summary>Task 13 wires this.</summary>
    public void ShowUpdateAvailable() => _update.Visible = true;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the window keeps the companion running in the tray. Quit is in the tray icon's menu.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
