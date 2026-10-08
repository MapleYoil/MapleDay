using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MapleDay.Services;

/// <summary>Draw a circle containing 1 at native icon resolution; never modify the app's source icon.</summary>
public static class UpdateBadgeIcon
{
    public static Bitmap Render(int size, string? baseIcon = null)
    {
        if (size < 16 || size > 256) throw new ArgumentOutOfRangeException(nameof(size));
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        if (baseIcon is not null)
        {
            using var icon = new Icon(baseIcon, size, size);
            graphics.DrawIcon(icon, new Rectangle(0, 0, size, size));
        }
        var diameter = baseIcon is null ? size - 1f : size * .625f;
        var left = size - diameter - .5f;
        using var brush = new SolidBrush(Color.FromArgb(58, 131, 247));
        using var outline = new Pen(Color.White, Math.Max(1, size / 32f));
        graphics.FillEllipse(brush, left, left, diameter, diameter);
        graphics.DrawEllipse(outline, left, left, diameter, diameter);
        using var font = new Font("Segoe UI", diameter * .68f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("1", font, Brushes.White, new RectangleF(left, left - diameter * .035f, diameter, diameter), format);
        return bitmap;
    }

    public static nint Create(int size, string? baseIcon = null)
    {
        using var bitmap = Render(size, baseIcon);
        return bitmap.GetHicon();
    }

    public static void Destroy(nint icon) { if (icon != 0) DestroyIcon(icon); }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
