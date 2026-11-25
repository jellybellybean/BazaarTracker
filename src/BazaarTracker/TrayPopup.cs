using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BazaarTracker;

/// <summary>
/// A small popup "bubble" near the tray: slides in at the screen corner, doesn't steal focus,
/// stays for a few seconds (doesn't disappear while the mouse is over it), a click runs the action.
/// </summary>
public sealed class TrayPopup : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmRound = 2;

    private const int PopupWidth = 340;
    private const int Pad = 14;
    private const int AccentWidth = 5;
    private const int SlideDistance = 22;
    private const int FadeInMs = 200;
    private const int FadeOutMs = 300;
    private const int ScreenMargin = 12;

    private enum Phase { In, Hold, Out }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = new();
    private readonly Action? _onClick;
    private readonly int _holdMs;

    private Phase _phase = Phase.In;
    private long _phaseStartedAt;
    private int _targetY;
    private int _slideDirection = 1;

    public TrayPopup(string title, string body, string? hint, Color accent, Action? onClick, int holdSeconds = 10)
    {
        _onClick = onClick;
        _holdMs = Math.Max(2, holdSeconds) * 1000;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(33, 36, 43);
        Opacity = 0;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;

        var textLeft = AccentWidth + Pad;
        var textWidth = PopupWidth - textLeft - Pad - 14;
        var y = 12;

        var accentBar = new Panel { Dock = DockStyle.Left, Width = AccentWidth, BackColor = accent };
        Controls.Add(accentBar);

        var close = new Label
        {
            Text = "×",
            AutoSize = false,
            Size = new Size(22, 22),
            Location = new Point(PopupWidth - 26, 6),
            Font = new Font("Segoe UI", 12f),
            ForeColor = Color.FromArgb(150, 156, 168),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter
        };
        close.Click += (_, _) => Dismiss();
        Controls.Add(close);

        AddLabel(title, new Font("Segoe UI", 10.5f, FontStyle.Bold), Color.White, textLeft, textWidth, ref y);
        y += 2;
        AddLabel(body, new Font("Segoe UI", 9.5f), Color.FromArgb(212, 216, 224), textLeft, textWidth, ref y);
        if (!string.IsNullOrWhiteSpace(hint))
        {
            y += 6;
            AddLabel(hint!, new Font("Segoe UI", 8.5f), Color.FromArgb(140, 146, 158), textLeft, textWidth, ref y);
        }

        ClientSize = new Size(PopupWidth, y + 12);

        // a click anywhere (except "×") runs the action
        Click += OnBodyClick;
        foreach (Control child in Controls)
        {
            if (!ReferenceEquals(child, close))
            {
                child.Click += OnBodyClick;
            }
        }

        _timer.Tick += OnTick;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Windows 11: system rounded corners; on Windows 10 a regular region with a radius
        try
        {
            var preference = DwmRound;
            DwmSetWindowAttribute(Handle, DwmWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
            // no dwmapi - stays a rectangle
        }

        if (Environment.OSVersion.Version.Build < 22000)
        {
            using var path = RoundedRect(new Rectangle(0, 0, Width, Height), 10);
            Region = new Region(path);
        }
    }

    /// <summary>Shows the window near the tray (work-area corner, next to the taskbar) and starts the animation.</summary>
    public void ShowAtTray()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;

        var taskbarOnTop = work.Top > bounds.Top;
        var taskbarOnLeft = work.Left > bounds.Left;

        var x = taskbarOnLeft ? work.Left + ScreenMargin : work.Right - Width - ScreenMargin;
        _targetY = taskbarOnTop ? work.Top + ScreenMargin : work.Bottom - Height - ScreenMargin;
        _slideDirection = taskbarOnTop ? -1 : 1;

        Location = new Point(x, _targetY + _slideDirection * SlideDistance);
        Show();

        _clock.Restart();
        _phaseStartedAt = 0;
        _timer.Start();
    }

    /// <summary>Fades the window out.</summary>
    public void Dismiss()
    {
        if (_phase == Phase.Out || IsDisposed)
        {
            return;
        }

        _phase = Phase.Out;
        _phaseStartedAt = _clock.ElapsedMilliseconds;
    }

    private void OnBodyClick(object? sender, EventArgs e)
    {
        try
        {
            _onClick?.Invoke();
        }
        catch
        {
            // the action must not crash the app
        }

        Dismiss();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.ElapsedMilliseconds;
        var elapsed = now - _phaseStartedAt;

        switch (_phase)
        {
            case Phase.In:
            {
                var t = Math.Min(1.0, elapsed / (double)FadeInMs);
                var eased = 1 - Math.Pow(1 - t, 3);
                Opacity = eased;
                Top = _targetY + (int)Math.Round(_slideDirection * SlideDistance * (1 - eased));
                if (t >= 1)
                {
                    _phase = Phase.Hold;
                    _phaseStartedAt = now;
                }

                break;
            }
            case Phase.Hold:
                if (Bounds.Contains(Cursor.Position))
                {
                    _phaseStartedAt = now; // mouse over the window - don't hide
                }
                else if (elapsed >= _holdMs)
                {
                    _phase = Phase.Out;
                    _phaseStartedAt = now;
                }

                break;
            case Phase.Out:
            {
                var t = Math.Min(1.0, elapsed / (double)FadeOutMs);
                Opacity = 1 - t;
                if (t >= 1)
                {
                    _timer.Stop();
                    Close();
                }

                break;
            }
        }
    }

    private void AddLabel(string text, Font font, Color color, int left, int width, ref int y)
    {
        var label = new Label
        {
            AutoSize = false,
            Text = text,
            Font = font,
            ForeColor = color,
            BackColor = Color.Transparent
        };
        var size = label.GetPreferredSize(new Size(width, 0));
        label.Size = new Size(width, size.Height);
        label.Location = new Point(left, y);
        Controls.Add(label);
        y += label.Height;
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
