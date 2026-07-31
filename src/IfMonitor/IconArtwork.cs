using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IfMonitor;

/// <summary>Draws the app icon with a real alpha channel (no baked checkerboard).</summary>
public static class IconArtwork
{
    public static readonly Color OkBarColor = Color.FromArgb(255, 34, 197, 94);
    public static readonly Color AlertBarColor = Color.FromArgb(255, 220, 60, 60);
    public static readonly Color AlertAltBarColor = Color.FromArgb(255, 230, 140, 0);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Bitmap Render(int size, Color? barColor = null)
    {
        Color fill = barColor ?? OkBarColor;
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;

        float margin = size * 0.08f;
        float inner = size - margin * 2;
        float gap = inner * 0.10f;
        float barW = (inner - gap * 2) / 3f;
        float x0 = margin;
        float baseY = margin + inner;

        using var brush = new SolidBrush(fill);
        g.FillRectangle(brush, x0, baseY - inner * 0.42f, barW, inner * 0.42f);
        g.FillRectangle(brush, x0 + barW + gap, baseY - inner * 0.68f, barW, inner * 0.68f);
        g.FillRectangle(brush, x0 + (barW + gap) * 2, baseY - inner * 0.94f, barW, inner * 0.94f);

        return bmp;
    }

    public static Icon ToIcon(int size, Color? barColor = null)
    {
        using var bmp = Render(size, barColor);
        IntPtr handle = bmp.GetHicon();
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
}
