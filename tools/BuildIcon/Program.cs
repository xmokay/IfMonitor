using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

string assets = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(assets);

string pngPath = Path.Combine(assets, "icon.png");
string alertPngPath = Path.Combine(assets, "icon-alert.png");
string icoPath = Path.Combine(assets, "IfMonitor.ico");

if (!File.Exists(pngPath))
{
    Console.Error.WriteLine($"Missing {pngPath} — add the NIC artwork PNG first.");
    return 1;
}

using var source = new Bitmap(pngPath);
using (var alert = CreateAlertArtwork(source))
{
    alert.Save(alertPngPath, ImageFormat.Png);
}

int[] sizes = [16, 32, 48, 256];
using var iconStream = new MemoryStream();
using var writer = new BinaryWriter(iconStream);

writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)sizes.Length);

var imageData = new List<byte[]>();
int offset = 6 + 16 * sizes.Length;

foreach (int size in sizes)
{
    using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, size, size));
    }

    using var pngStream = new MemoryStream();
    bmp.Save(pngStream, ImageFormat.Png);
    byte[] data = pngStream.ToArray();
    imageData.Add(data);

    writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write((uint)data.Length);
    writer.Write((uint)offset);
    offset += data.Length;
}

foreach (byte[] data in imageData)
{
    writer.Write(data);
}

await File.WriteAllBytesAsync(icoPath, iconStream.ToArray());
Console.WriteLine($"Wrote {icoPath} from {pngPath} ({iconStream.Length} bytes)");
return 0;

static Bitmap CreateAlertArtwork(Bitmap source)
{
    var alert = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

    for (int y = 0; y < source.Height; y++)
    {
        for (int x = 0; x < source.Width; x++)
        {
            Color pixel = source.GetPixel(x, y);
            alert.SetPixel(x, y, IsGreen(pixel) ? MapGreenToRed(pixel) : pixel);
        }
    }

    return alert;
}

static bool IsGreen(Color pixel)
{
    // Only the PCB/chip are green. White border/shadow, blue bracket, and orange
    // contacts fail this test and are preserved exactly.
    return pixel.A > 0
        && pixel.G > pixel.R + 12
        && pixel.G > pixel.B + 4;
}

static Color MapGreenToRed(Color pixel)
{
    // Preserve the original alpha and brightness so anti-aliased edges and shadows
    // remain identical to the green artwork.
    int red = pixel.G;
    int green = pixel.G / 5;
    int blue = pixel.G / 5;
    return Color.FromArgb(pixel.A, red, green, blue);
}
