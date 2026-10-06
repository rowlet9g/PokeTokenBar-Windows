using System.Drawing;
using System.Drawing.Imaging;
using PokeTokenBar.Platform.Windows;

namespace PokeTokenBar.Core.Tests;

public sealed class PokemonTrayIconTests
{
    [Fact]
    public void Icon_crops_transparent_padding_and_retains_the_sprite_color()
    {
        using var source = new Bitmap(96, 96, PixelFormat.Format32bppArgb);
        for (var y = 40; y < 56; y++)
            for (var x = 40; x < 56; x++) source.SetPixel(x, y, Color.OrangeRed);
        using var stream = new MemoryStream();
        source.Save(stream, ImageFormat.Png);
        using var icon = PokemonTrayIcon.Create(stream.ToArray());
        Assert.Equal(new Size(32, 32), icon.Size);
        using var rendered = icon.ToBitmap();
        Assert.Equal(Color.OrangeRed.ToArgb(), rendered.GetPixel(16, 16).ToArgb());
        Assert.True(rendered.GetPixel(1, 1).A > 0);
    }

    [Fact]
    public void Invisible_or_invalid_sprites_are_rejected_without_exposing_a_native_handle()
    {
        using var source = new Bitmap(2, 2);
        using var stream = new MemoryStream();
        source.Save(stream, ImageFormat.Png);
        Assert.Throws<InvalidDataException>(() => PokemonTrayIcon.Create(stream.ToArray()));
        Assert.Throws<InvalidDataException>(() => PokemonTrayIcon.Create(new byte[] { 1, 2, 3 }));
    }
}
