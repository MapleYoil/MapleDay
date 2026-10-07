using MapleDay.Core;
using Windows.Graphics.Imaging;

namespace MapleDay.Services;

public sealed class CharacterImageLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly SemaphoreSlim DownloadSlots = new(8, 8);

    public async Task<CroppedCharacterImage?> LoadAsync(string? imageUrl, CancellationToken token)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        await DownloadSlots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var bytes = await Http.GetByteArrayAsync(uri, token).ConfigureAwait(false);
            return await DecodeAsync(bytes, token).ConfigureAwait(false);
        }
        finally { DownloadSlots.Release(); }
    }

    public static async Task<CroppedCharacterImage> DecodeAsync(byte[] imageBytes, CancellationToken token)
    {
        using var memory = new MemoryStream(imageBytes);
        using var stream = memory.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
        var pixelData = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return CharacterImageCrop.CropReferenceFrame(pixelData.DetachPixelData(), checked((int)decoder.PixelWidth), checked((int)decoder.PixelHeight));
    }
}
