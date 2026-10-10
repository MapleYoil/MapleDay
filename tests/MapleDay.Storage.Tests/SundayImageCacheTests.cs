using MapleDay.Services;
using SkiaSharp;
using System.Net;

namespace MapleDay.Storage.Tests;

public sealed class SundayImageCacheTests
{
    [Theory]
    [InlineData(500)]
    [InlineData(2000)]
    public void CropKeepsVariableHeightEffectsAndRemovesBannerAndFooter(int panelHeight)
    {
        using var image = Panel(panelHeight);
        var bounds = SundayImageCache.FindEffects(image)!.Value;
        Assert.InRange(bounds.Top, 580, 600); Assert.InRange(bounds.Bottom, 700 + panelHeight, 720 + panelHeight);
        using var original = SKImage.FromBitmap(image); using var bytes = original.Encode(SKEncodedImageFormat.Png, 100);
        using var cropped = SKBitmap.Decode(SundayImageCache.Crop(bytes.ToArray())!);
        Assert.True(cropped.Height < image.Height - 500); Assert.InRange(cropped.Width, 800, 840);
    }

    [Fact]
    public void RefusesUnrecognizedOrInvalidImagesInsteadOfCuttingOffBenefits()
    {
        using var image = new SKBitmap(876, 1400); image.Erase(SKColors.Black);
        Assert.Null(SundayImageCache.FindEffects(image)); Assert.Null(SundayImageCache.Crop([1, 2, 3]));
    }

    [Fact]
    public async Task KeepsPixelsOnlyForThisSessionAndRejectsUnofficialUrls()
    {
        using var image = Panel(500); using var encodedImage = SKImage.FromBitmap(image); using var data = encodedImage.Encode(SKEncodedImageFormat.Png, 100);
        var handler = new Images(data.ToArray()); using var http = new HttpClient(handler); var cache = new SundayImageCache(http);
        var first = await cache.LoadAsync("https://lwi.nexon.com/effects.png"); Assert.NotNull(first);
        Assert.Same(first, await cache.LoadAsync("https://lwi.nexon.com/effects.png")); Assert.Equal(1, handler.Count);
        Assert.Null(await cache.LoadAsync("https://invalid.example/image.png")); Assert.Equal(1, handler.Count);
        cache.Clear(); Assert.NotNull(await cache.LoadAsync("https://lwi.nexon.com/effects.png")); Assert.Equal(2, handler.Count);
    }

    [Fact]
    public void CleansOnlyLegacyGeneratedImagesAndKeepsOtherFiles()
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mapleday-sunday-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            var old = Path.Combine(directory, new string('A', 64) + ".png"); File.WriteAllText(old, "old");
            var keep = Path.Combine(directory, "unrelated.png"); File.WriteAllText(keep, "keep");
            SundayImageCache.RemoveLegacyImages(directory);
            Assert.False(File.Exists(old)); Assert.Equal("keep", File.ReadAllText(keep));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static SKBitmap Panel(int height)
    {
        var image = new SKBitmap(876, 1000 + height); image.Erase(SKColors.DarkSlateBlue);
        using var canvas = new SKCanvas(image); using var paint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(35, 600, 840, 700 + height), 50, 50, paint);
        return image;
    }
    private sealed class Images(byte[] bytes) : HttpMessageHandler
    {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Count++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(bytes) }); }
    }
}
