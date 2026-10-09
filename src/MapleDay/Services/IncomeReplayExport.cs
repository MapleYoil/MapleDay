using System.Diagnostics;
using System.Threading.Channels;
using System.Runtime.InteropServices;
using MapleDay.Core;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MapleDay.Services;

public static class IncomeReplayExport
{
    public static double ReplayTime(double elapsed, double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0 || duration > 600) throw new ArgumentOutOfRangeException(nameof(duration));
        return Math.Clamp(double.IsFinite(elapsed) ? elapsed : 0, 0, duration) / duration * IncomeReplayRenderer.Duration;
    }
    public static async Task ExportImageAsync(IncomeReplay replay, IncomeDisplay display, string scope, string assets,
        string destination, bool webp, CancellationToken cancellationToken = default)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".mapleday-export-" + Guid.NewGuid().ToString("N") + (webp ? ".webp" : ".png"));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var renderer = new IncomeReplayRenderer(replay, display, scope, assets);
            var pixels = renderer.RenderPixels(IncomeReplayRenderer.Duration);
            using var image = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(1920, 1080, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque));
            Marshal.Copy(pixels, 0, image.GetPixels(), pixels.Length);
            using var pixmap = image.PeekPixels();
            using var encoded = webp
                ? pixmap.Encode(new SkiaSharp.SKWebpEncoderOptions(SkiaSharp.SKWebpEncoderCompression.Lossless, 100))
                : image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            if (encoded is null) throw new IOException("결과 이미지를 만들지 못했어요.");
            await File.WriteAllBytesAsync(temporary, encoded.ToArray(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static async Task ExportAsync(IncomeReplay replay, IncomeDisplay display, string scope, string assets,
        string encoderPath, string destination, bool gif, IProgress<double>? progress = null, CancellationToken cancellationToken = default,
        double duration = IncomeReplayRenderer.Duration)
    {
        _ = ReplayTime(0, duration);
        // Finish beside the chosen destination, then replace it atomically; failures never leave a partial final video.
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".mapleday-export-" + Guid.NewGuid().ToString("N") + (gif ? ".gif" : ".mp4"));
        try
        {
            if (gif) await GifAsync(replay, display, scope, assets, temporary, progress, cancellationToken, duration);
            else await Mp4Async(replay, display, scope, assets, encoderPath, temporary, progress, cancellationToken, duration);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task Mp4Async(IncomeReplay replay, IncomeDisplay display, string scope, string assets,
        string encoderPath, string path, IProgress<double>? progress, CancellationToken token, double duration)
    {
        const int width = 1920, height = 1080, fps = 60;
        using var renderer = new IncomeReplayRenderer(replay, display, scope, assets);
        var start = new ProcessStartInfo(encoderPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
        foreach (var arg in new[] { path, width.ToString(), height.ToString(), fps.ToString() }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("MP4 인코더를 시작하지 못했어요.");
        using var stop = token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } });
        var errors = process.StandardError.ReadToEndAsync(token);
        try
        {
            var frames = (int)Math.Ceiling(duration * fps);
            using var pipelineCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var pipelineToken = pipelineCancellation.Token;
            var ready = Channel.CreateBounded<byte[]>(2);
            var free = Channel.CreateBounded<byte[]>(3);
            for (var i = 0; i < 3; i++) free.Writer.TryWrite(new byte[width * height * 4]);
            var render = Task.Run(async () =>
            {
                try
                {
                    for (var frame = 0; frame < frames; frame++)
                    {
                        var buffer = await free.Reader.ReadAsync(pipelineToken);
                        renderer.RenderPixels(frame / (double)Math.Max(1, frames - 1) * IncomeReplayRenderer.Duration, width, height, buffer: buffer);
                        await ready.Writer.WriteAsync(buffer, pipelineToken);
                    }
                    ready.Writer.TryComplete();
                }
                catch (Exception error) { ready.Writer.TryComplete(error); throw; }
            }, CancellationToken.None);
            try
            {
                var written = 0;
                await foreach (var buffer in ready.Reader.ReadAllAsync(pipelineToken))
                {
                    await process.StandardInput.BaseStream.WriteAsync(buffer, pipelineToken);
                    free.Writer.TryWrite(buffer);
                    if (++written % 6 == 0 || written == frames) progress?.Report(written / (double)frames);
                }
                await render;
            }
            finally
            {
                pipelineCancellation.Cancel();
                try { await render; } catch (OperationCanceledException) when (pipelineToken.IsCancellationRequested) { }
            }
            process.StandardInput.Close(); await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) throw new IOException("Windows 영상 인코더에서 MP4를 만들지 못했어요. " + await errors);
        }
        catch (IOException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(CancellationToken.None); } }
    }
    private static async Task GifAsync(IncomeReplay replay, IncomeDisplay display, string scope, string assets,
        string path, IProgress<double>? progress, CancellationToken token, double duration)
    {
        const int width = 1280, height = 720, fps = 25;
        using var renderer = new IncomeReplayRenderer(replay, display, scope, assets);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.FailIfExists);
        using (var output = await file.OpenAsync(FileAccessMode.ReadWrite))
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.GifEncoderId, output);
            var frames = (int)Math.Ceiling(duration * fps);
            byte[]? previous = null;
            for (var frame = 0; frame < frames; frame++)
            {
                token.ThrowIfCancellationRequested();
                var pixels = renderer.RenderPixels(frame / (double)Math.Max(1, frames - 1) * IncomeReplayRenderer.Duration, width, height);
                var region = ChangedPixels(pixels, previous, width, height);
                previous = pixels;
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, region.Width, region.Height, 96, 96, region.Pixels);
                await encoder.BitmapProperties.SetPropertiesAsync(new Dictionary<string, BitmapTypedValue>
                { ["/grctlext/Delay"] = new((ushort)4, PropertyType.UInt16), ["/grctlext/Disposal"] = new((byte)1, PropertyType.UInt8),
                    ["/imgdesc/Left"] = new(region.Left, PropertyType.UInt16), ["/imgdesc/Top"] = new(region.Top, PropertyType.UInt16) });
                if (frame + 1 < frames) await encoder.GoToNextFrameAsync(); else await encoder.FlushAsync();
                if (frame % 3 == 0) progress?.Report((frame + 1d) / frames);
            }
        }
        // NETSCAPE loop extension follows the logical screen's global palette.
        using var input = File.OpenRead(path);
        var header = new byte[13]; input.ReadExactly(header);
        if ((header[10] & 0x80) != 0)
        {
            var palette = new byte[3 * (1 << ((header[10] & 7) + 1))]; input.ReadExactly(palette);
            header = header.Concat(palette).ToArray();
        }
        var loopPath = path + ".loop";
        try
        {
            await using (var loop = File.Create(loopPath))
            {
                await loop.WriteAsync(header, token);
                await loop.WriteAsync(new byte[] { 0x21, 0xff, 0x0b, 0x4e, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2e, 0x30, 0x03, 0x01, 0, 0, 0 }, token);
                await input.CopyToAsync(loop, token);
            }
            input.Close(); File.Move(loopPath, path, true);
        }
        finally { if (File.Exists(loopPath)) File.Delete(loopPath); }
    }
    private sealed record GifRegion(ushort Left, ushort Top, ushort Width, ushort Height, byte[] Pixels);
    private static GifRegion ChangedPixels(byte[] current, byte[]? previous, int width, int height)
    {
        if (previous is null) return new(0, 0, (ushort)width, (ushort)height, current);
        var pixels = MemoryMarshal.Cast<byte, uint>(current); var old = MemoryMarshal.Cast<byte, uint>(previous);
        var left = width; var right = -1; var top = height; var bottom = -1;
        for (var y = 0; y < height; y++)
        {
            if (pixels.Slice(y * width, width).SequenceEqual(old.Slice(y * width, width))) continue;
            top = Math.Min(top, y); bottom = y;
            for (var x = 0; x < width; x++) if (pixels[y * width + x] != old[y * width + x])
            { left = Math.Min(left, x); right = Math.Max(right, x); }
        }
        if (right < 0) return new(0, 0, 1, 1, current[..4]);
        var regionWidth = right - left + 1; var regionHeight = bottom - top + 1;
        var changed = new byte[regionWidth * regionHeight * 4];
        for (var y = 0; y < regionHeight; y++)
            current.AsSpan(((top + y) * width + left) * 4, regionWidth * 4).CopyTo(changed.AsSpan(y * regionWidth * 4));
        return new((ushort)left, (ushort)top, (ushort)regionWidth, (ushort)regionHeight, changed);
    }
}
