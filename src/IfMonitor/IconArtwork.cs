using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IfMonitor;

public enum IconStyle
{
    Ok,
    Alert,
    Warn,
}

/// <summary>Loads separate green/red/orange NIC artwork PNGs (no runtime color replacement).</summary>
public static class IconArtwork
{
    private const string OkResourceName = "IfMonitor.Assets.icon.png";
    private const string AlertResourceName = "IfMonitor.Assets.icon-alert.png";
    private const string WarnResourceName = "IfMonitor.Assets.icon-warn.png";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon ToIcon(int size, bool alert = false) =>
        ToIcon(size, alert ? IconStyle.Alert : IconStyle.Ok);

    public static Icon ToIcon(int size, IconStyle style)
    {
        using var bmp = Render(size, style);
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

    public static Bitmap Render(int size, bool alert = false) =>
        Render(size, alert ? IconStyle.Alert : IconStyle.Ok);

    public static Bitmap Render(int size, IconStyle style)
    {
        using var source = LoadMaster(style);
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, size, size));
        return bmp;
    }

    private static Bitmap LoadMaster(IconStyle style)
    {
        (string resource, string fileName) = style switch
        {
            IconStyle.Alert => (AlertResourceName, "icon-alert.png"),
            IconStyle.Warn => (WarnResourceName, "icon-warn.png"),
            _ => (OkResourceName, "icon.png"),
        };

        using Stream? stream = typeof(IconArtwork).Assembly.GetManifestResourceStream(resource);
        if (stream is not null)
        {
            return new Bitmap(stream);
        }

        string fallback = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (File.Exists(fallback))
        {
            return new Bitmap(fallback);
        }

        throw new InvalidOperationException($"Embedded {fileName} not found.");
    }
}
