using System.Drawing;
using System.Drawing.Drawing2D;

namespace Murmur.App;

/// <summary>Generates the three tray icons at runtime: idle, recording and transcribing.</summary>
public static class TrayIcons
{
    public static Icon Idle { get; } = Build(Color.FromArgb(160, 160, 170), filled: false);
    public static Icon Recording { get; } = Build(Color.FromArgb(230, 70, 70), filled: true);
    public static Icon Transcribing { get; } = Build(Color.FromArgb(90, 160, 240), filled: true);

    private static Icon Build(Color color, bool filled)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var pen = new Pen(color, 2.5f);
            using var brush = new SolidBrush(color);

            var capsule = new Rectangle(11, 5, 10, 15);
            if (filled) g.FillEllipse(brush, capsule);
            else g.DrawEllipse(pen, capsule);

            g.DrawArc(pen, new Rectangle(7, 13, 18, 13), 0, 180);
            g.DrawLine(pen, 16, 24, 16, 28);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
