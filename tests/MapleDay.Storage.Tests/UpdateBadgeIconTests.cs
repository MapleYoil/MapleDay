using System.Drawing;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class UpdateBadgeIconTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void Overlay_has_transparent_corners_and_a_readable_circle_and_digit(int size)
    {
        using var image = UpdateBadgeIcon.Render(size);
        Assert.Equal(size, image.Width);
        Assert.Equal(size, image.Height);
        Assert.Equal(0, image.GetPixel(0, 0).A);
        var colors = Enumerable.Range(0, size * size).Select(p => image.GetPixel(p % size, p / size)).ToArray();
        Assert.Contains(colors, c => c.A > 240 && c.B > 220 && c.R < 100);
        // White pixels inside the circle are the digit, rather than just its outline.
        Assert.Contains(Enumerable.Range(size / 3, size / 3).SelectMany(y =>
            Enumerable.Range(size / 3, size / 3).Select(x => image.GetPixel(x, y))),
            c => c.A > 240 && c.R > 240 && c.G > 240 && c.B > 240);
    }

    [Fact]
    public void Tray_badge_preserves_the_base_icon_outside_the_overlay()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "mapleday.ico");
        using var image = UpdateBadgeIcon.Render(32, path);
        using var icon = new Icon(path, 32, 32);
        using var original = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(original)) graphics.DrawIcon(icon, new Rectangle(0, 0, 32, 32));
        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 32; x++) Assert.Equal(original.GetPixel(x, y), image.GetPixel(x, y));
        Assert.Contains(Enumerable.Range(12, 20).SelectMany(y => Enumerable.Range(12, 20).Select(x => (x, y))),
            point => original.GetPixel(point.x, point.y) != image.GetPixel(point.x, point.y));
    }

    [Fact]
    public void Generated_native_icon_is_valid_without_creating_a_window()
    {
        var handle = UpdateBadgeIcon.Create(16);
        try
        {
            Assert.NotEqual(0, handle);
            using var icon = Icon.FromHandle(handle);
            Assert.Equal(new Size(16, 16), icon.Size);
            using var bitmap = icon.ToBitmap();
            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        }
        finally { UpdateBadgeIcon.Destroy(handle); }
    }
}
