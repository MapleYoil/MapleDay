using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MapleDay.Services;

// Memory-only GDI rendering preserves the pixel font's classic hinting.
// The supplied font is a real Bold face; no WinUI FontWeight is applied.
public sealed class SchedulerLabelPixels : IDisposable
{
    private readonly string _fontPath;
    private readonly nint _font;
    public SchedulerLabelPixels(string fontPath)
    {
        _fontPath = fontPath;
        if (AddFontResourceExW(fontPath, 0x10, 0) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _font = CreateFontW(-13, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 4, 0, "NanumGothic");
        if (_font == 0) { RemoveFontResourceExW(fontPath, 0x10, 0); throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }

    public byte[] Render(string text, int width, int height, byte red, byte green, byte blue, double strokeStrength = 1.5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!double.IsFinite(strokeStrength) || strokeStrength <= 0) throw new ArgumentOutOfRangeException(nameof(strokeStrength));
        var pixels = new byte[checked(width * height * 4)];
        var dc = CreateCompatibleDC(0);
        if (dc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint bitmap = 0, previousBitmap = 0, previousFont = 0;
        try
        {
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
            if (bitmap == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            previousBitmap = SelectObject(dc, bitmap);
            previousFont = SelectObject(dc, _font);
            // All scheduler name rectangles lie inside this flat part of the WZ row.
            for (var offset = 0; offset < pixels.Length; offset += 4)
            { pixels[offset] = 214; pixels[offset + 1] = 212; pixels[offset + 2] = 209; }
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            SetTextColor(dc, (uint)(red | green << 8 | blue << 16));
            SetBkColor(dc, 209u | 212u << 8 | 214u << 16);
            var rectangle = new NativeRect { Right = width, Bottom = height };
            DrawTextW(dc, text, text.Length, ref rectangle, 0x20 | 0x800 | 0x8000);
            GdiFlush();
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            // Strengthen partial edge coverage without moving glyphs or changing
            // their advances. The actual Bold face retains its original outlines.
            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = Strengthen(pixels[offset], 214, blue, strokeStrength);
                pixels[offset + 1] = Strengthen(pixels[offset + 1], 212, green, strokeStrength);
                pixels[offset + 2] = Strengthen(pixels[offset + 2], 209, red, strokeStrength);
                pixels[offset + 3] = 255;
            }
            return pixels;
        }
        finally
        {
            if (previousFont != 0) SelectObject(dc, previousFont);
            if (previousBitmap != 0) SelectObject(dc, previousBitmap);
            if (bitmap != 0) DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    private static byte Strengthen(byte value, byte background, byte foreground, double strength)
        => (byte)Math.Clamp(background + (int)Math.Round((value - background) * strength),
            Math.Min(background, foreground), Math.Max(background, foreground));

    public void Dispose() { DeleteObject(_font); RemoveFontResourceExW(_fontPath, 0x10, 0); }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPixels, YPixels; public uint ColorsUsed, ColorsImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int AddFontResourceExW(string path, uint flags, nint reserved);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool RemoveFontResourceExW(string path, uint flags, nint reserved);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint outputPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(nint dc, uint color);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(nint dc, string text, int count, ref NativeRect rectangle, uint flags);
}
