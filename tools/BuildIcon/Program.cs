using System.Drawing;
using System.Drawing.Imaging;
using IfMonitor;

string assets = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(assets);

string pngPath = Path.Combine(assets, "icon.png");
string icoPath = Path.Combine(assets, "IfMonitor.ico");

using (var icon = IconArtwork.Render(256))
{
    icon.Save(pngPath, ImageFormat.Png);
}

Console.WriteLine($"Wrote {pngPath} (programmatic, true alpha)");

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
    using var bmp = IconArtwork.Render(size);
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
Console.WriteLine($"Wrote {icoPath} ({iconStream.Length} bytes)");
return 0;
