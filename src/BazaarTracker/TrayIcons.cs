using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BazaarTracker;

/// <summary>Что показывает иконка в трее.</summary>
public enum TrayState
{
    /// <summary>Идёт подключение (серый кружок).</summary>
    Connecting,

    /// <summary>Нет связи или входа (красный «!»).</summary>
    Error,

    /// <summary>Лобби и очередь пусты (тёмный кружок «0»).</summary>
    Idle,

    /// <summary>В очереди кто-то есть (янтарный, число игроков).</summary>
    Queue,

    /// <summary>Есть открытые лобби со свободными местами (зелёный, число лобби).</summary>
    Lobby
}

/// <summary>
/// Рисует иконку для трея на лету: цветной кружок с числом. Для состояний «Queue» и «Lobby»
/// кадры анимации — расходящееся кольцо и лёгкое «дыхание» цвета.
/// </summary>
internal static class TrayIcons
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static bool IsAlert(TrayState state) => state is TrayState.Queue or TrayState.Lobby;

    /// <param name="phase">Фаза анимации 0..1 (учитывается только при <paramref name="animate"/>).</param>
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

                // в начале каждого «удара» кружок чуть светлее
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
                // небольшой сдвиг вниз: у Segoe UI центр строки визуально выше центра цифр
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
        // на 16 пикселях «9+» не читается, поэтому для больших чисел просто «+»
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
