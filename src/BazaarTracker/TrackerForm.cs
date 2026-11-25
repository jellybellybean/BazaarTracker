using System.Diagnostics;
using System.Media;

namespace BazaarTracker;

public sealed class TrackerForm : Form
{
    private static readonly Color GreenBack = Color.FromArgb(226, 246, 235);
    private static readonly Color GreenText = Color.FromArgb(7, 105, 62);
    private static readonly Color AmberBack = Color.FromArgb(255, 246, 219);
    private static readonly Color AmberText = Color.FromArgb(128, 78, 0);
    private static readonly Color RedBack = Color.FromArgb(253, 231, 233);
    private static readonly Color RedText = Color.FromArgb(155, 20, 35);
    private static readonly Color GrayBack = Color.FromArgb(240, 241, 243);
    private static readonly Color GrayText = Color.FromArgb(70, 74, 80);

    private readonly TrackerService _service;
    private readonly bool _startHidden;
    private readonly System.Windows.Forms.Timer _ticker = new() { Interval = 1000 };
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _knownLobbies = new(StringComparer.OrdinalIgnoreCase);

    private AppSettings _settings;

    private Label _bannerTitle = null!;
    private Label _bannerSub = null!;
    private Panel _bannerPanel = null!;
    private Label _statusLabel = null!;
    private ListView _list = null!;
    private RichTextBox _log = null!;
    private NotifyIcon _tray = null!;
    private Button _refreshButton = null!;

    private readonly System.Windows.Forms.Timer _trayAnim = new() { Interval = 120 };
    private TrayState _trayState = TrayState.Connecting;
    private int _trayCount;
    private double _trayPhase;
    private Icon? _trayIcon;
    private TrayPopup? _popup;

    private int? _lastQueueCount;
    private DateTime _nextPollAt = DateTime.MinValue;
    private bool _polling;
    private bool _exiting;
    private bool _trayHintShown;

    public TrackerForm(bool startHidden)
    {
        _startHidden = startHidden;
        _settings = AppSettings.Load();
        _service = new TrackerService(_settings, SessionData.Load());
        _service.Log += AppendLog;

        BuildUi();
        Load += OnFormLoad;
        FormClosing += OnFormClosing;
        Resize += OnResize;
    }

    private void BuildUi()
    {
        SuspendLayout();

        Text = "BazaarTracker — открытые лобби The Bazaar (PTR)";
        MinimumSize = new Size(680, 520);
        Size = new Size(780, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;
        BackColor = Color.White;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10),
            BackColor = Color.White
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        // ── баннер статуса ────────────────────────────────────────────────
        _bannerPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8) };
        _bannerTitle = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 34,
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            Text = "Запуск…"
        };
        _bannerSub = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = new Font("Segoe UI", 9f),
            Text = "Подключаюсь к Steam и серверу The Bazaar."
        };
        _bannerPanel.Controls.Add(_bannerSub);
        _bannerPanel.Controls.Add(_bannerTitle);
        root.Controls.Add(_bannerPanel, 0, 0);

        // ── список лобби ──────────────────────────────────────────────────
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        _list.Columns.Add("Лобби", 255);
        _list.Columns.Add("Хост", 140);
        _list.Columns.Add("Игроки", 65, HorizontalAlignment.Center);
        _list.Columns.Add("Код", 95, HorizontalAlignment.Center);
        _list.Columns.Add("Осталось", 110);
        _list.DoubleClick += (_, _) => CopySelectedCode();
        root.Controls.Add(_list, 0, 1);

        // ── журнал ────────────────────────────────────────────────────────
        _log = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f),
            BackColor = Color.FromArgb(250, 250, 251),
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = false
        };
        root.Controls.Add(_log, 0, 2);

        // ── нижняя панель ─────────────────────────────────────────────────
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9f),
            ForeColor = GrayText,
            Text = "…"
        };
        footer.Controls.Add(_statusLabel, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Fill
        };
        _refreshButton = MakeButton("Обновить (F5)", (_, _) => TriggerPollNow());
        var settingsButton = MakeButton("Настройки", (_, _) => OpenSettings());
        var trayButton = MakeButton("В трей", (_, _) => HideToTray());
        buttons.Controls.Add(_refreshButton);
        buttons.Controls.Add(settingsButton);
        buttons.Controls.Add(trayButton);
        footer.Controls.Add(buttons, 1, 0);

        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                TriggerPollNow();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                HideToTray();
            }
        };

        BuildTrayIcon();
        SetBanner("Запуск…", "Подключаюсь к Steam и серверу The Bazaar.", GrayBack, GrayText);
        ResumeLayout();
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(110, 30),
            Margin = new Padding(6, 0, 0, 0)
        };
        button.Click += onClick;
        return button;
    }

    private void BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Обновить", null, (_, _) => TriggerPollNow());
        menu.Items.Add("Сменить аккаунт", null, (_, _) => SwitchAccount());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "BazaarTracker — The Bazaar (PTR)",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        _trayAnim.Tick += (_, _) =>
        {
            _trayPhase = (_trayPhase + 0.08) % 1.0;
            RenderTrayIcon();
        };
        SetTrayState(TrayState.Connecting, 0, "BazaarTracker — подключение…");
    }

    /// <summary>Меняет вид иконки и подсказку в трее; для «живых» состояний включает анимацию.</summary>
    private void SetTrayState(TrayState state, int count, string tooltip)
    {
        _trayState = state;
        _trayCount = count;
        _tray.Text = tooltip.Length > 63 ? tooltip[..62] + "…" : tooltip;

        var animate = _settings.AnimateTrayIcon && TrayIcons.IsAlert(state);
        if (!animate)
        {
            _trayPhase = 0;
        }

        _trayAnim.Enabled = animate;
        RenderTrayIcon();
    }

    private void RenderTrayIcon()
    {
        if (_tray == null || _exiting)
        {
            return;
        }

        var icon = TrayIcons.Create(
            _trayState,
            _trayCount,
            _trayPhase,
            _settings.AnimateTrayIcon && _trayAnim.Enabled);
        var old = _trayIcon;
        _tray.Icon = icon;
        _trayIcon = icon;
        old?.Dispose();
    }

    private async void OnFormLoad(object? sender, EventArgs e)
    {
        if (_startHidden || _settings.StartMinimized)
        {
            BeginInvoke(new Action(HideToTray));
        }

        AppendLog($"Версия приложения {Application.ProductVersion}; сервер: {_settings.TournamentBaseUrl}");
        StartTicker();

        if (!await _service.EnsureAuthenticatedAsync(force: false, _cts.Token))
        {
            SetBanner(
                "Нет входа в аккаунт",
                "Steam должен быть запущен, а на аккаунте — куплена The Bazaar. Повторю попытку автоматически.",
                RedBack,
                RedText);
            AppendLog("Авторизация не выполнена — попробую снова при следующем обновлении.");
            SetTrayState(TrayState.Error, 0, "BazaarTracker — нет входа в аккаунт");
            _nextPollAt = DateTime.UtcNow.AddSeconds(_settings.ClampedInterval);
            return;
        }

        UpdateAccountStatus();
        await PollAsync();
    }

    private void StartTicker()
    {
        _ticker.Tick -= OnTick;
        _ticker.Tick += OnTick;
        _ticker.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_polling)
        {
            return;
        }

        var secondsLeft = (int)Math.Ceiling((_nextPollAt - DateTime.UtcNow).TotalSeconds);
        if (secondsLeft <= 0)
        {
            await PollAsync();
        }
        else
        {
            UpdateStatusLine(secondsLeft);
        }
    }

    private void TriggerPollNow()
    {
        if (_polling)
        {
            return;
        }

        _nextPollAt = DateTime.MinValue;
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;
        _refreshButton.Enabled = false;
        var started = DateTime.Now;
        try
        {
            var result = await _service.PollAsync(_cts.Token);
            var duration = (int)(DateTime.Now - started).TotalMilliseconds;

            if (result.LobbyError != null)
            {
                SetBanner("Не удалось получить список лобби", result.LobbyError, RedBack, RedText);
                AppendLog("Ошибка: " + result.LobbyError);
                SetTrayState(TrayState.Error, 0, "BazaarTracker — нет связи с сервером");
            }
            else
            {
                ApplyResult(result, duration);
            }
        }
        catch (OperationCanceledException)
        {
            // приложение закрывается
        }
        catch (Exception ex)
        {
            SetBanner("Ошибка приложения", ex.Message, RedBack, RedText);
            AppendLog("Исключение: " + ex);
        }
        finally
        {
            _polling = false;
            if (!_cts.IsCancellationRequested && !IsDisposed)
            {
                _refreshButton.Enabled = true;
                _nextPollAt = DateTime.UtcNow.AddSeconds(_settings.ClampedInterval);
                UpdateStatusLine(_settings.ClampedInterval);
            }
        }
    }

    private void ApplyResult(PollResult result, int durationMs)
    {
        if (IsDisposed)
        {
            return;
        }

        var lobbies = result.Lobbies
            .Where(l => !string.IsNullOrWhiteSpace(l.Code) || !string.IsNullOrWhiteSpace(l.LobbyId))
            .OrderBy(l => l.FreeSlots == 0)
            .ThenByDescending(l => l.JoinedCount)
            .ThenBy(l => l.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var lobby in lobbies)
        {
            var item = new ListViewItem(lobby.DisplayName) { Tag = lobby };
            item.SubItems.Add(string.IsNullOrWhiteSpace(lobby.HostUsername) ? "—" : lobby.HostUsername);
            item.SubItems.Add(lobby.PlayersText);
            item.SubItems.Add(lobby.DisplayCode);
            item.SubItems.Add(DescribeExpiry(lobby.ExpiresAt));
            if (lobby.FreeSlots == 0)
            {
                item.ForeColor = Color.Gray;
            }

            _list.Items.Add(item);
        }

        _list.EndUpdate();

        var freeSlots = lobbies.Sum(l => l.FreeSlots);
        if (lobbies.Length == 0)
        {
            var queueText = result.Queue is { } q && q.MaxPlayers > 0
                ? $" В очереди на автоподбор: {q.QueuedCount} из {q.MaxPlayers}."
                : string.Empty;
            SetBanner(
                "Открытых лобби нет",
                "Публичных лобби сейчас никто не создал." + queueText + " Проверяю снова автоматически.",
                AmberBack,
                AmberText);
        }
        else
        {
            var fullCount = lobbies.Count(l => l.FreeSlots == 0);
            var sub = $"{lobbies.Length} шт., свободных мест: {freeSlots}." +
                      (fullCount > 0 ? $" Заполненных: {fullCount}." : string.Empty) +
                      " Двойной клик — скопировать код лобби.";
            SetBanner($"Открытых лобби: {lobbies.Length}", sub, GreenBack, GreenText);
        }

        var keys = new HashSet<string>(
            lobbies.Select(l => string.IsNullOrWhiteSpace(l.Code) ? l.LobbyId! : l.Code!),
            StringComparer.OrdinalIgnoreCase);
        var newLobbies = lobbies
            .Where(l => !_knownLobbies.Contains(string.IsNullOrWhiteSpace(l.Code) ? l.LobbyId! : l.Code!))
            .ToArray();

        var hadKnown = _knownLobbies.Count > 0;
        _knownLobbies.Clear();
        _knownLobbies.UnionWith(keys);

        var lobbyAlert = newLobbies.Length > 0 && (hadKnown || result.Lobbies.Length > 0);

        // Очередь: сообщаем, когда игроков в ней стало больше, чем при прошлой проверке
        // (в том числе при первой проверке, если очередь уже не пуста).
        string? queueAlertText = null;
        if (result.Queue is { } q2)
        {
            if (_settings.QueueNotificationsEnabled &&
                q2.QueuedCount > 0 &&
                q2.QueuedCount > (_lastQueueCount ?? 0))
            {
                queueAlertText = q2.MaxPlayers > 0
                    ? $"В очереди: {q2.QueuedCount} из {q2.MaxPlayers}"
                    : $"В очереди: {q2.QueuedCount}";
            }

            _lastQueueCount = q2.QueuedCount;
        }

        if (lobbyAlert)
        {
            NotifyAboutLobbies(newLobbies, queueAlertText);
        }
        else if (queueAlertText != null)
        {
            NotifyAboutQueue(queueAlertText);
        }

        var queueInfo = result.Queue is { } queue && queue.MaxPlayers > 0
            ? $" · очередь {queue.QueuedCount}/{queue.MaxPlayers}"
            : string.Empty;
        var queueError = result.QueueError != null ? " · " + result.QueueError : string.Empty;
        AppendLog($"Обновлено за {durationMs} мс: лобби {lobbies.Length}, свободных мест {freeSlots}{queueInfo}.{queueError}");

        var openLobbies = lobbies.Count(l => l.FreeSlots > 0);
        var queued = result.Queue?.QueuedCount ?? 0;
        var tooltip = $"BazaarTracker — лобби: {openLobbies}{queueInfo}";
        if (openLobbies > 0)
        {
            SetTrayState(TrayState.Lobby, openLobbies, tooltip);
        }
        else if (queued > 0)
        {
            SetTrayState(TrayState.Queue, queued, tooltip);
        }
        else
        {
            SetTrayState(TrayState.Idle, 0, tooltip);
        }
    }

    private void NotifyAboutQueue(string text)
    {
        if (_settings.NotificationsEnabled)
        {
            ShowAlert(
                "Кто-то в очереди",
                text,
                TrayState.Queue,
                WillLaunchGame ? "Клик — запустить игру" : "Клик — открыть окно",
                () =>
                {
                    if (!LaunchGameIfNeeded())
                    {
                        RestoreFromTray();
                    }
                });
        }

        if (_settings.SoundEnabled)
        {
            SystemSounds.Exclamation.Play();
        }
    }

    /// <summary>
    /// Показывает оповещение: всплывающее окно у трея (по умолчанию) или обычный balloon Windows.
    /// </summary>
    private void ShowAlert(string title, string body, TrayState kind, string? hint, Action? onClick)
    {
        if (!_settings.UseTrayPopup)
        {
            _tray.ShowBalloonTip(10_000, title, body, ToolTipIcon.Info);
            return;
        }

        if (_popup is { IsDisposed: false } previous)
        {
            previous.Close();
        }

        var popup = new TrayPopup(title, body, hint, TrayIcons.ColorFor(kind), onClick);
        popup.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_popup, popup))
            {
                _popup = null;
            }
        };
        _popup = popup;
        popup.ShowAtTray();
    }

    /// <summary>Игру нужно запускать по клику: включено в настройках и она ещё не запущена.</summary>
    private bool WillLaunchGame => _settings.LaunchGameOnClick && !IsGameRunning();

    private static bool IsGameRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("TheBazaar");
            var running = processes.Length > 0;
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return running;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Запускает игру через Steam (ветка — та, что выбрана в Steam, у вас PTR). Возвращает true, если запуск начат.</summary>
    private bool LaunchGameIfNeeded()
    {
        if (!WillLaunchGame)
        {
            return false;
        }

        try
        {
            // Пока трекер жив (и держит SteamAPI с AppID игры), Steam считает, что игра уже запущена,
            // и второй экземпляр не стартует. Поэтому запуск делает отдельный скрытый процесс:
            // он ждёт, пока завершится трекер, и только потом открывает steam://rungameid.
            var pid = Environment.ProcessId;
            var url = "steam://rungameid/" + AppInfo.SteamAppIdText;
            var script = $"Wait-Process -Id {pid} -ErrorAction SilentlyContinue; " +
                         "Start-Sleep -Milliseconds 1000; " +
                         $"Start-Process '{url}'";
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", script },
                UseShellExecute = false,
                CreateNoWindow = true
            })?.Dispose();
            AppendLog("Закрываю трекер и запускаю The Bazaar через Steam…");
            ExitApp();
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("Не удалось запустить игру: " + ex.Message);
            return false;
        }
    }

    private void CopyCode(string code)
    {
        try
        {
            // copy: true — данные остаются в буфере и после закрытия трекера
            Clipboard.SetDataObject(code, true);
            AppendLog($"Код {code} скопирован в буфер обмена.");
        }
        catch
        {
            // буфер обмена может быть занят другим приложением
        }
    }

    private void NotifyAboutLobbies(LobbyDto[] lobbies, string? queueText = null)
    {
        var preview = lobbies.Length == 1
            ? $"{lobbies[0].DisplayName} — {lobbies[0].PlayersText}"
            : string.Join(", ", lobbies.Take(3).Select(l => l.DisplayName)) +
              (lobbies.Length > 3 ? $" и ещё {lobbies.Length - 3}" : string.Empty);
        if (queueText != null)
        {
            preview += ". " + queueText;
        }

        if (_settings.NotificationsEnabled)
        {
            var withCode = lobbies.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Code));
            if (withCode != null)
            {
                var code = withCode.DisplayCode;
                ShowAlert(
                    $"Открылось лобби: {lobbies.Length}",
                    preview,
                    TrayState.Lobby,
                    WillLaunchGame
                        ? $"Клик — скопировать код {code} и запустить игру"
                        : $"Клик — скопировать код {code}",
                    () =>
                    {
                        CopyCode(code);
                        LaunchGameIfNeeded();
                    });
            }
            else
            {
                ShowAlert(
                    $"Открылось лобби: {lobbies.Length}",
                    preview,
                    TrayState.Lobby,
                    WillLaunchGame ? "Клик — запустить игру" : "Клик — открыть окно",
                    () =>
                    {
                        if (!LaunchGameIfNeeded())
                        {
                            RestoreFromTray();
                        }
                    });
            }
        }

        if (_settings.SoundEnabled)
        {
            SystemSounds.Exclamation.Play();
        }
    }

    private void CopySelectedCode()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }

        if (_list.SelectedItems[0].Tag is not LobbyDto lobby)
        {
            return;
        }

        var code = lobby.DisplayCode;
        if (code == "—")
        {
            return;
        }

        try
        {
            Clipboard.SetText(code);
            AppendLog($"Код {code} скопирован в буфер обмена.");
            SetStatusSuffix($"код {code} скопирован");
        }
        catch
        {
            // буфер обмена может быть занят другим приложением
        }
    }

    private static string DescribeExpiry(DateTimeOffset? expiresAt)
    {
        if (expiresAt is not { } expires)
        {
            return "—";
        }

        var left = expires - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            return "истекает";
        }

        if (left < TimeSpan.FromMinutes(1))
        {
            return "меньше минуты";
        }

        if (left < TimeSpan.FromHours(1))
        {
            return $"{(int)left.TotalMinutes} мин";
        }

        return $"{(int)left.TotalHours} ч {left.Minutes:00} м";
    }

    private void SetBanner(string title, string subtitle, Color back, Color fore)
    {
        _bannerPanel.BackColor = back;
        _bannerTitle.ForeColor = fore;
        _bannerSub.ForeColor = fore;
        _bannerTitle.Text = title;
        _bannerSub.Text = subtitle;
    }

    private void UpdateAccountStatus()
    {
        var who = string.IsNullOrWhiteSpace(_service.Session.UserName) ? "аккаунт" : _service.Session.UserName;
        Text = $"BazaarTracker — открытые лобби The Bazaar ({who})";
        _tray.Text = $"BazaarTracker — лобби The Bazaar ({who})";
    }

    private void UpdateStatusLine(int secondsLeft)
    {
        var who = string.IsNullOrWhiteSpace(_service.Session.UserName) ? "—" : _service.Session.UserName;
        var server = _settings.TournamentBaseUrl.Contains("ptr", StringComparison.OrdinalIgnoreCase) ? "PTR" : "prod";
        var next = Math.Max(0, secondsLeft);
        _statusLabel.Text = $"{who} · {server} · проверка через {next} с";
    }

    private void SetStatusSuffix(string suffix)
    {
        _statusLabel.Text += " · " + suffix;
    }

    private void AppendLog(string message)
    {
        if (_log.IsDisposed)
        {
            return;
        }

        if (_log.InvokeRequired)
        {
            _log.BeginInvoke(new Action(() => AppendLog(message)));
            return;
        }

        if (_log.Lines.Length > 400)
        {
            _log.Clear();
        }

        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings = dialog.Settings;
        _settings.Save();
        _service.ApplySettings(_settings);
        SetTrayState(_trayState, _trayCount, _tray.Text);
        AppendLog($"Настройки сохранены: интервал {_settings.ClampedInterval} с, уведомления " +
                  $"{(_settings.NotificationsEnabled ? "вкл" : "выкл")}.");
        _nextPollAt = DateTime.MinValue;
        TriggerPollNow();
    }

    private async void SwitchAccount()
    {
        AppendLog("Смена аккаунта: закрываю сессию и вхожу заново через Steam.");
        _knownLobbies.Clear();
        _lastQueueCount = null;
        try
        {
            if (await _service.SwitchAccountAsync(_cts.Token))
            {
                UpdateAccountStatus();
                AppendLog($"Вход выполнен: {_service.Session.UserName}.");
                await PollAsync();
            }
            else
            {
                SetBanner(
                    "Не удалось войти",
                    "Steam должен быть запущен, а на аккаунте — куплена The Bazaar. Нажмите «Обновить», чтобы повторить.",
                    RedBack,
                    RedText);
            }
        }
        catch (OperationCanceledException)
        {
            // приложение закрывается
        }
    }

    private void HideToTray()
    {
        Hide();
        if (!_trayHintShown && _settings.NotificationsEnabled)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(5000, "BazaarTracker работает", "Приложение свёрнуто в трей и продолжает проверять лобби.", ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
        {
            HideToTray();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        Shutdown();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void Shutdown()
    {
        _exiting = true;
        _ticker.Stop();
        _trayAnim.Stop();
        if (_popup is { IsDisposed: false })
        {
            _popup.Close();
        }

        _cts.Cancel();
        _tray.Visible = false;
        _tray.Icon = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        SteamAuth.Shutdown();
        _service.Dispose();
        _cts.Dispose();
    }
}
