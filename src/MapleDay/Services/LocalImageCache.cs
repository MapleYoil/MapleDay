using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MapleDay.Services;

// The prepared pixels and bitmap stay in memory when controls are scrolled or recreated.
// All callers run on the UI thread; no URI loading or asynchronous PNG decoding occurs.
public static class LocalImageCache
{
    private static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Decoded");
    private static readonly Dictionary<string, ImageInfo> Manifest = JsonSerializer.Deserialize<Dictionary<string, ImageInfo>>(
        File.ReadAllText(Path.Combine(DirectoryPath, "manifest.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    private static readonly Dictionary<string, WriteableBitmap> Images = new(StringComparer.Ordinal);

    public static WriteableBitmap Get(string file)
    {
        var key = file.Replace("ms-appx:///Assets/", "", StringComparison.Ordinal).Replace('\\', '/');
        if (Images.TryGetValue(key, out var cached)) return cached;
        var info = Manifest[key];
        var bytes = File.ReadAllBytes(Path.Combine(DirectoryPath, info.File));
        if (bytes.Length != checked(info.Width * info.Height * 4)) throw new InvalidDataException($"Invalid image pixels: {key}");
        var bitmap = new WriteableBitmap(info.Width, info.Height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(bytes);
        bitmap.Invalidate();
        Images.Add(key, bitmap);
        return bitmap;
    }

    public sealed record ImageInfo(int Width, int Height, string File);
}

public sealed class CachedImage : DependencyObject
{
    public static readonly DependencyProperty FileProperty = DependencyProperty.RegisterAttached("File", typeof(string), typeof(CachedImage), new PropertyMetadata(null, Changed));
    public static string? GetFile(DependencyObject sender) => (string?)sender.GetValue(FileProperty);
    public static void SetFile(DependencyObject sender, string? value) => sender.SetValue(FileProperty, value);
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is Image image) image.Source = args.NewValue is string file && file.Length > 0 ? LocalImageCache.Get(file) : null;
    }
}
