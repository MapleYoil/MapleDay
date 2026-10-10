using MapleDay.Core;
using SkiaSharp;

namespace MapleDay.Services;

public sealed class SundayImageCache(HttpClient http)
{
    private readonly Dictionary<string, byte[]> _images = [];
    public void Clear() => _images.Clear();

    public static void RemoveLegacyImages(string? directory = null)
    {
        var root = Path.GetFullPath(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay", "sunday-images"));
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
        foreach (var file in Directory.EnumerateFiles(root))
        {
            var resolved = Path.GetFullPath(file);
            if (!resolved.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            var name = Path.GetFileName(file);
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[0-9A-F]{64}\.png(?:\.[0-9a-f]{32}\.tmp)?\z",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) File.Delete(file);
        }
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root, recursive: false);
    }

    public async Task<byte[]?> LoadAsync(string url, CancellationToken token = default)
    {
        if (!ScheduleCalendar.OfficialUri(url)) return null;
        if (_images.TryGetValue(url, out var existing)) return existing;
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (!ScheduleCalendar.OfficialUri(response.RequestMessage?.RequestUri?.AbsoluteUri)) return null;
        const int maximum = 20 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximum) return null;
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var bytes = new MemoryStream(); var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + read > maximum) return null;
            bytes.Write(buffer, 0, read);
        }
        var cropped = await Task.Run(() => Crop(bytes.ToArray()), token).ConfigureAwait(false);
        if (cropped is null) return null;
        token.ThrowIfCancellationRequested();
        // Keep a small session-only working set. Image bytes never go to disk.
        if (_images.Count >= 8) _images.Remove(_images.Keys.First());
        _images[url] = cropped;
        return cropped;
    }

    public static byte[]? Crop(byte[] bytes)
    {
        using var stream = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width < 200 || codec.Info.Height < 200
            || (long)codec.Info.Width * codec.Info.Height > 40_000_000) return null;
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null || FindEffects(bitmap) is not { } bounds) return null;
        using var cropped = new SKBitmap(bounds.Width, bounds.Height);
        if (!bitmap.ExtractSubset(cropped, bounds)) return null;
        using var image = SKImage.FromBitmap(cropped);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    // The benefits panel has two matching bright vertical borders. Detect their
    // longest paired span rather than assuming a fixed percentage of image height.
    // Extra space above/below retains rounded corners and all benefit footnotes.
    public static SKRectI? FindEffects(SKBitmap bitmap)
    {
        var width = bitmap.Width; var height = bitmap.Height;
        var longest = 0; var bestX = 0; var bestStart = 0; var bestEnd = 0;
        static bool Bright(SKColor color) => color.Alpha > 220 && Math.Min(color.Red, Math.Min(color.Green, color.Blue)) > 215
            && Math.Max(color.Red, Math.Max(color.Green, color.Blue)) - Math.Min(color.Red, Math.Min(color.Green, color.Blue)) < 30;
        for (var x = Math.Max(1, (int)(width * .015)); x < width * .12; x++)
        {
            var start = -1;
            for (var y = (int)(width * .5); y <= height; y++)
            {
                var paired = y < height && Bright(bitmap.GetPixel(x, y)) && Bright(bitmap.GetPixel(width - x - 1, y));
                if (paired && start < 0) start = y;
                if (paired || start < 0) continue;
                if (y - start > longest) { longest = y - start; bestX = x; bestStart = start; bestEnd = y; }
                start = -1;
            }
        }
        if (longest < width * .22) return null;
        var verticalPadding = (int)Math.Ceiling(width * .055); var horizontalPadding = Math.Max(4, width / 100);
        return new(Math.Max(0, bestX - horizontalPadding), Math.Max(0, bestStart - verticalPadding),
            Math.Min(width, width - bestX + horizontalPadding), Math.Min(height, bestEnd + verticalPadding));
    }
}
