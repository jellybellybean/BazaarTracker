namespace BazaarTracker;

public sealed class SettingsForm : Form
{
    private readonly NumericUpDown _interval = new()
    {
        Minimum = 5,
        Maximum = 300,
        Width = 64,
        Increment = 5,
        TextAlign = HorizontalAlignment.Right
    };

    private readonly CheckBox _notify = new() { Text = "Show a notification when a lobby appears", AutoSize = true };
    private readonly CheckBox _popup = new() { Text = "Show a popup near the tray (instead of the system notification)", AutoSize = true };
    private readonly CheckBox _launchGame = new() { Text = "Launch the game when the popup is clicked (if it isn't running)", AutoSize = true };
    private readonly CheckBox _notifyQueue = new() { Text = "Show a notification when players appear in the queue", AutoSize = true };
    private readonly CheckBox _sound = new() { Text = "Play a sound with the notification", AutoSize = true };
    private readonly CheckBox _animateTray = new() { Text = "Animate the tray icon when there are lobbies or a queue", AutoSize = true };
    private readonly CheckBox _tray = new() { Text = "Minimize to tray instead of closing", AutoSize = true };
    private readonly CheckBox _autoStart = new() { Text = "Start with Windows", AutoSize = true };
    private readonly CheckBox _startMinimized = new() { Text = "Start minimized to tray", AutoSize = true };
    private readonly TextBox _coreUrl = new() { Width = 472 };
    private readonly TextBox _tournamentUrl = new() { Width = 472 };

    public SettingsForm(AppSettings settings)
    {
        Settings = settings;

        Text = "BazaarTracker settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(512, 634);
        Font = new Font("Segoe UI", 9f);

        _interval.Value = settings.ClampedInterval;
        _notify.Checked = settings.NotificationsEnabled;
        _notifyQueue.Checked = settings.QueueNotificationsEnabled;
        _popup.Checked = settings.UseTrayPopup;
        _launchGame.Checked = settings.LaunchGameOnClick;
        _sound.Checked = settings.SoundEnabled;
        _animateTray.Checked = settings.AnimateTrayIcon;
        _tray.Checked = settings.MinimizeToTray;
        _autoStart.Checked = AutoStart.IsEnabled();
        _startMinimized.Checked = settings.StartMinimized;
        _coreUrl.Text = settings.CoreBaseUrl;
        _tournamentUrl.Text = settings.TournamentBaseUrl;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        // ── polling ─────────────────────────────────────────────────────────
        var options = new GroupBox
        {
            Text = "Polling",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10)
        };
        var optionsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 10,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        var intervalRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        intervalRow.Controls.Add(new Label { Text = "Check every", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        intervalRow.Controls.Add(_interval);
        intervalRow.Controls.Add(new Label { Text = "seconds (5–300)", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });

        optionsLayout.Controls.Add(intervalRow, 0, 0);
        optionsLayout.Controls.Add(_notify, 0, 1);
        optionsLayout.Controls.Add(_notifyQueue, 0, 2);
        optionsLayout.Controls.Add(_popup, 0, 3);
        optionsLayout.Controls.Add(_launchGame, 0, 4);
        optionsLayout.Controls.Add(_sound, 0, 5);
        optionsLayout.Controls.Add(_animateTray, 0, 6);
        optionsLayout.Controls.Add(_tray, 0, 7);
        optionsLayout.Controls.Add(_autoStart, 0, 8);
        optionsLayout.Controls.Add(_startMinimized, 0, 9);
        options.Controls.Add(optionsLayout);

        // ── API addresses ────────────────────────────────────────────────────
        var endpoints = new GroupBox
        {
            Text = "Server (change only if the API address changed)",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };
        var endpointsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        endpointsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        endpointsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        endpointsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        endpointsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        endpointsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        endpointsLayout.Controls.Add(new Label { Text = "Account API (temponet):", AutoSize = true }, 0, 0);
        endpointsLayout.Controls.Add(_coreUrl, 0, 1);
        endpointsLayout.Controls.Add(new Label
        {
            Text = "Lobby API (tournament):",
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
        }, 0, 2);
        endpointsLayout.Controls.Add(_tournamentUrl, 0, 3);
        endpointsLayout.Controls.Add(new Label
        {
            Text = "Default: The Bazaar PTR server.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 10, 0, 0)
        }, 0, 4);
        endpoints.Controls.Add(endpointsLayout);

        // ── buttons ────────────────────────────────────────────────────────
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var cancel = new Button { Text = "Cancel", Size = new Size(96, 30), DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "Save", Size = new Size(110, 30) };
        var reset = new Button { Text = "Defaults", Size = new Size(130, 30) };
        ok.Click += (_, _) => SaveAndClose();
        reset.Click += (_, _) =>
        {
            _coreUrl.Text = AppInfo.DefaultCoreBaseUrl;
            _tournamentUrl.Text = AppInfo.DefaultTournamentBaseUrl;
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(reset);

        root.Controls.Add(options, 0, 0);
        root.Controls.Add(endpoints, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public AppSettings Settings { get; private set; }

    private void SaveAndClose()
    {
        var core = _coreUrl.Text.Trim().TrimEnd('/');
        var tournament = _tournamentUrl.Text.Trim().TrimEnd('/');
        if (!Uri.TryCreate(core, UriKind.Absolute, out var coreUri) || coreUri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(this, "The account API address must be an absolute https URL.", "BazaarTracker");
            return;
        }

        if (!Uri.TryCreate(tournament, UriKind.Absolute, out var tournamentUri) || tournamentUri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(this, "The lobby API address must be an absolute https URL.", "BazaarTracker");
            return;
        }

        Settings = new AppSettings
        {
            PollIntervalSeconds = (int)_interval.Value,
            NotificationsEnabled = _notify.Checked,
            QueueNotificationsEnabled = _notifyQueue.Checked,
            UseTrayPopup = _popup.Checked,
            LaunchGameOnClick = _launchGame.Checked,
            SoundEnabled = _sound.Checked,
            AnimateTrayIcon = _animateTray.Checked,
            MinimizeToTray = _tray.Checked,
            StartWithWindows = _autoStart.Checked,
            StartMinimized = _startMinimized.Checked,
            CoreBaseUrl = core,
            TournamentBaseUrl = tournament
        };

        if (!AutoStart.Apply(_autoStart.Checked) && _autoStart.Checked)
        {
            MessageBox.Show(
                this,
                "Could not register autostart in the registry. The other settings were saved.",
                "BazaarTracker");
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
