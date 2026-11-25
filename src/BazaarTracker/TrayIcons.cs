using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BazaarTracker;

/// <summary>What the tray icon shows.</summary>
public enum TrayState
{
    /// <summary>Connecting (gray dot).</summary>
    Connecting,

    /// <summary>No connection or sign-in (red "!").</summary>
    Error,

    /// <summary>Lobbies and queue are empty (dark dot "0").</summary>
    Idle,

    /// <summary>Someone is in the queue (amber, player count).</summary>
    Queue,

    /// <summary>Open lobbies with free slots (green, lobby count).</summary>
    Lobby
}

/// <summary>
/// Draws the tray icon on the fly: a colored dot with a number. For the "Queue" and "Lobby" states
/// animation frames are an expanding ring and a slight color "breathing".
/// </summary>
internal static class TrayIcons
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static bool IsAlert(TrayState state) => state is TrayState.Queue or TrayState.Lobby;

    /// <param name="phase">Animation phase 0..1 (used only when <paramref name="animate"/> is set).</param>
    public static Icon Create(TrayState state, int count, double phase, bool animate)
    {
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            var baseColor = ColorFor(state);
            var center = size / 2f;
            var radius = size * 0.31f;
            var maxRing = size / 2f - 1f;

            if (animate && IsAlert(state))
            {
                var t = (float)Math.Clamp(phase, 0, 1);
                var ringRadius = radius + t * (maxRing - radius);
                var alpha = (int)(210 * (1 - t));
                using var ringPen = new Pen(Color.FromArgb(alpha, baseColor), Math.Max(1.5f, size / 10f));
                g.DrawEllipse(ringPen, center - ringRadius, center - ringRadius, ringRadius * 2, ringRadius * 2);

                // at the start of each "beat" the dot is slightly lighter
                baseColor = Blend(baseColor, Color.White, 0.45f * (1 - t));
            }

            using (var fill = new SolidBrush(baseColor))
            using (var outline = new Pen(Blend(baseColor, Color.Black, 0.40f), Math.Max(1f, size / 16f)))
            {
                g.FillEllipse(fill, center - radius, center - radius, radius * 2, radius * 2);
                g.DrawEllipse(outline, center - radius, center - radius, radius * 2, radius * 2);
            }

            var label = LabelFor(state, count);
            if (label.Length > 0)
            {
                var fontPx = size * 0.52f;
                using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
                using var textBrush = new SolidBrush(state == TrayState.Queue ? Color.FromArgb(50, 32, 0) : Color.White);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                // small downward shift: in Segoe UI the line center sits visually above the digit center
                g.DrawString(label, font, textBrush, new RectangleF(0, size * 0.04f, size, size), format);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static string LabelFor(TrayState state, int count) => state switch
    {
        TrayState.Error => "!",
        TrayState.Connecting => string.Empty,
        // "9+" is unreadable at 16 pixels, so large numbers just show "+"
        _ => count > 9 ? "+" : count.ToString()
    };

    public static Color ColorFor(TrayState state) => state switch
    {
        TrayState.Lobby => Color.FromArgb(30, 170, 90),
        TrayState.Queue => Color.FromArgb(245, 166, 35),
        TrayState.Error => Color.FromArgb(205, 50, 62),
        TrayState.Idle => Color.FromArgb(88, 98, 114),
        _ => Color.FromArgb(150, 155, 162)
    };

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            from.A,
            (int)(from.R + (to.R - from.R) * amount),
            (int)(from.G + (to.G - from.G) * amount),
            (int)(from.B + (to.B - from.B) * amount));
    }
}
