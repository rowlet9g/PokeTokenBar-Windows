using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PokeTokenBar.Platform.Windows;

public static class PokemonTrayIcon
{
    public static Icon Create(byte[] png)
    {
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            using var source = new Bitmap(stream);
            if (source.Width > 2048 || source.Height > 2048) throw new InvalidDataException("Sprite dimensions are too large.");
            var left = source.Width;
            var top = source.Height;
            var right = -1;
            var bottom = -1;
            for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                    if (source.GetPixel(x, y).A > 0)
                    { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            if (right < left) throw new InvalidDataException("Sprite has no visible pixels.");
            var crop = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            using var target = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(target))
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                var scale = 32d / Math.Max(crop.Width, crop.Height);
                var width = Math.Max(1, (int)Math.Round(crop.Width * scale));
                var height = Math.Max(1, (int)Math.Round(crop.Height * scale));
                graphics.DrawImage(source, new Rectangle((32 - width) / 2, (32 - height) / 2, width, height), crop, GraphicsUnit.Pixel);
            }
            var handle = target.GetHicon();
            try
            {
                using var borrowed = Icon.FromHandle(handle);
                return (Icon)borrowed.Clone();
            }
            finally { DestroyIcon(handle); }
        }
        catch (Exception error) when (error is ArgumentException or ExternalException)
        { throw new InvalidDataException("Sprite could not be converted to a tray icon.", error); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
