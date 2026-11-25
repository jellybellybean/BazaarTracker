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

    private readonly CheckBox _notify = new() { Text = "Показывать уведомление, когда появляется лобби", AutoSize = true };
    private readonly CheckBox _popup = new() { Text = "Показывать всплывающее окно у трея (вместо системного уведомления)", AutoSize = true };
    private readonly CheckBox _launchGame = new() { Text = "По клику на всплывающее окно запускать игру (если она не запущена)", AutoSize = true };
    private readonly CheckBox _notifyQueue = new() { Text = "Показывать уведомление, когда в очереди появляются игроки", AutoSize = true };
    private readonly CheckBox _sound = new() { Text = "Проигрывать звук вместе с уведомлением", AutoSize = true };
    private readonly CheckBox _animateTray = new() { Text = "Анимировать иконку в трее, когда есть лобби или очередь", AutoSize = true };
    private readonly CheckBox _tray = new() { Text = "Сворачивать в трей вместо закрытия", AutoSize = true };
    private readonly CheckBox _autoStart = new() { Text = "Запускать при входе в Windows", AutoSize = true };
    private readonly CheckBox _startMinimized = new() { Text = "Стартовать свёрнутым в трей", AutoSize = true };
    private readonly TextBox _coreUrl = new() { Width = 472 };
    private readonly TextBox _tournamentUrl = new() { Width = 472 };

    public SettingsForm(AppSettings settings)
    {
        Settings = settings;

        Text = "Настройки BazaarTracker";
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

        // ── опрос ─────────────────────────────────────────────────────────
        var options = new GroupBox
        {
            Text = "Опрос",
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
        intervalRow.Controls.Add(new Label { Text = "Проверять каждые", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
        intervalRow.Controls.Add(_interval);
        intervalRow.Controls.Add(new Label { Text = "секунд (5–300)", AutoSize = true, Margin = new Padding(6, 6, 0, 0) });

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

        // ── адреса API ────────────────────────────────────────────────────
        var endpoints = new GroupBox
        {
            Text = "Сервер (менять, только если сменился адрес API)",
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

        endpointsLayout.Controls.Add(new Label { Text = "API аккаунта (temponet):", AutoSize = true }, 0, 0);
        endpointsLayout.Controls.Add(_coreUrl, 0, 1);
        endpointsLayout.Controls.Add(new Label
        {
            Text = "API лобби (tournament):",
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
        }, 0, 2);
        endpointsLayout.Controls.Add(_tournamentUrl, 0, 3);
        endpointsLayout.Controls.Add(new Label
        {
            Text = "По умолчанию — PTR-сервер The Bazaar.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 10, 0, 0)
        }, 0, 4);
        endpoints.Controls.Add(endpointsLayout);

        // ── кнопки ────────────────────────────────────────────────────────
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var cancel = new Button { Text = "Отмена", Size = new Size(96, 30), DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "Сохранить", Size = new Size(110, 30) };
        var reset = new Button { Text = "По умолчанию", Size = new Size(130, 30) };
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
            MessageBox.Show(this, "Адрес API аккаунта должен быть абсолютным https-адресом.", "BazaarTracker");
            return;
        }

        if (!Uri.TryCreate(tournament, UriKind.Absolute, out var tournamentUri) || tournamentUri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(this, "Адрес API лобби должен быть абсолютным https-адресом.", "BazaarTracker");
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
                "Не удалось прописать автозапуск в реестре. Остальные настройки сохранены.",
                "BazaarTracker");
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
