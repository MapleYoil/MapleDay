using System.Runtime.InteropServices.WindowsRuntime;
using MapleDay.Core;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace MapleDay.Services;

/// <summary>Public item icons only. No API headers, disk cache, redirects, or character-image cropping.</summary>
internal sealed class EquipmentIconLoader
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _slots = new(6);
    private readonly Dictionary<string, WriteableBitmap> _cache = new(StringComparer.Ordinal);
    public void Clear() => _cache.Clear();

    public async Task<WriteableBitmap?> LoadAsync(string? url, CancellationToken token)
    {
        if (!EquipmentSlots.IsPublicIcon(url)) return null;
        if (_cache.TryGetValue(url!, out var cached)) return cached;
        await _slots.WaitAsync(token);
        try
        {
            if (_cache.TryGetValue(url!, out cached)) return cached;
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1_048_576) return null;
            using var memory = new MemoryStream();
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            {
                var buffer = new byte[8192]; int count;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                { if (memory.Length + count > 1_048_576) return null; await memory.WriteAsync(buffer.AsMemory(0, count), token); }
            }
            memory.Position = 0;
            using var stream = memory.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
            if (decoder.PixelWidth > 512 || decoder.PixelHeight > 512) return null;
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
            token.ThrowIfCancellationRequested();
            var bitmap = new WriteableBitmap((int)decoder.PixelWidth, (int)decoder.PixelHeight);
            using (var target = bitmap.PixelBuffer.AsStream()) target.Write(pixels.DetachPixelData());
            bitmap.Invalidate();
            if (_cache.Count >= 256) _cache.Clear();
            _cache[url!] = bitmap;
            return bitmap;
        }
        finally { _slots.Release(); }
    }
}
