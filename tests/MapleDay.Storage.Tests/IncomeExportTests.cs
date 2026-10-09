using System.Drawing;
using System.Drawing.Imaging;
using MapleDay.Core;
using MapleDay.Services;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;

namespace MapleDay.Storage.Tests;

public sealed class IncomeExportTests
{
    [Theory]
    [InlineData(2, 1, 5)]
    [InlineData(20, 5, 2.5)]
    [InlineData(600, 600, 10)]
    [InlineData(3, 9, 10)]
    public void Selected_duration_maps_to_the_complete_replay(double duration, double elapsed, double expected)
        => Assert.Equal(expected, IncomeReplayExport.ReplayTime(elapsed, duration));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Result_images_are_lossless_final_frames(bool webp)
    {
        var assets = Path.Combine(Root, "src", "MapleDay", "Assets");
        var output = Path.Combine(Root, "artifacts", "build", "replay-preview"); Directory.CreateDirectory(output);
        var destination = Path.Combine(output, webp ? "result.webp" : "result.png");
        await IncomeReplayExport.ExportImageAsync(Replay, new("both"), "전체 캐릭터", assets, destination, webp);
        using var decoded = SkiaSharp.SKBitmap.Decode(destination);
        Assert.Equal(1920, decoded.Width); Assert.Equal(1080, decoded.Height);
        var bytes = new byte[1920 * 1080 * 4]; System.Runtime.InteropServices.Marshal.Copy(decoded.GetPixels(), bytes, 0, bytes.Length);
        using var renderer = new IncomeReplayRenderer(Replay, new("both"), "전체 캐릭터", assets);
        Assert.Equal(renderer.RenderPixels(10), bytes);
        var originalFile = await File.ReadAllBytesAsync(destination);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => IncomeReplayExport.ExportImageAsync(Replay, new(), "", assets, destination, webp, cancellation.Token));
        Assert.Equal(originalFile, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(output, ".mapleday-export-*." + (webp ? "webp" : "png")));
    }
    [Fact]
    public void Dense_replay_records_render_timings_for_preview_and_export()
    {
        if (Environment.GetEnvironmentVariable("MAPLEDAY_REPLAY_BENCHMARK") != "1") return;
        var assets = Path.Combine(Root, "src", "MapleDay", "Assets");
        var replay = new IncomeReplay(Enumerable.Range(0, 100).Select(i =>
            new ReplayClear(new(2026, 10, 1), new[] { "스우", "루시드", "진 힐라", "발드릭스" }[i % 4], 50_000_000_000)));
        var measurements = new List<object>();
        foreach (var width in new[] { 1280, 1920 })
        {
            using var renderer = new IncomeReplayRenderer(replay, new("both"), "전체 캐릭터", assets);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var buffer = new byte[width * width * 9 / 16 * 4];
            for (var frame = 0; frame < 120; frame++)
                renderer.RenderPixels(frame / 119d * 10, width, width * 9 / 16, buffer: buffer);
            measurements.Add(new { width, elapsedMs = watch.Elapsed.TotalMilliseconds, frameMs = watch.Elapsed.TotalMilliseconds / 120 });
        }
        var output = Path.Combine(Root, "artifacts", "build", "replay-preview"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "benchmark-" + (Environment.GetEnvironmentVariable("MAPLEDAY_BENCHMARK_LABEL") ?? "latest") + ".json"),
            System.Text.Json.JsonSerializer.Serialize(measurements));
    }
    [Fact]
    public void Hunting_replay_drops_each_fragment_and_only_earned_meso_coins()
    {
        var assets = Path.Combine(Root, "src", "MapleDay", "Assets");
        var replay = HuntingIncome.Replay([
            new("hunt-1", "owner", new(2026, 10, 8), 874_000_000, 15, 5_000_000),
            new("hunt-2", "owner", new(2026, 10, 9), 874_000_000, 15, 5_000_000)
        ], _ => "사냥 캐릭터");
        using var rain = new IncomeMesoRain(replay, assets);
        Assert.Equal(30, rain.LootDropCount);
        Assert.Equal(1, rain.GoldDropCount);
        Assert.Equal(1_000_000_000, rain.RepresentedMeso);
        using var renderer = new IncomeReplayRenderer(replay, new("both"), "사냥 기록", assets);
        Assert.Equal(1280 * 720 * 4, renderer.RenderPixels(10, 1280, 720).Length);
    }
    [Fact]
    public void Loot_settlements_drop_all_actual_items_without_generating_meso_coins()
    {
        var assets = Path.Combine(Root, "src", "MapleDay", "Assets");
        var drops = BossLootCatalog.ForBoss("유피테르", "hard")
            .Select(item => new ReplayLoot(item.Name, 10_000_000_000, item.Icon)).ToArray();
        var clear = new ReplayClear(new(2026, 10, 8), "유피테르", drops.Sum(item => item.Meso), drops);
        using var lootRain = new IncomeMesoRain(new IncomeReplay([clear]), assets);
        Assert.Equal(drops.Length, lootRain.LootDropCount);
        Assert.Equal(0, lootRain.GoldDropCount); Assert.Equal(0, lootRain.RepresentedMeso);
        using var mixed = new IncomeMesoRain(new IncomeReplay([clear with { Meso = clear.Meso + 2_500_000_000 }]), assets);
        Assert.Equal(drops.Length, mixed.LootDropCount);
        Assert.Equal(2, mixed.GoldDropCount); Assert.Equal(2_000_000_000, mixed.RepresentedMeso);
        var output = Path.Combine(Root, "artifacts", "build", "replay-preview"); Directory.CreateDirectory(output);
        using var renderer = new IncomeReplayRenderer(new IncomeReplay([clear]), new("meso"), "물욕템 시연", assets);
        using var image = renderer.Render(10);
        image.Save(Path.Combine(output, "loot-rain.png"), ImageFormat.Png);
    }
    private static string Root => Directory.GetParent(typeof(IncomeExportTests).Assembly.Location)!.Ancestors()
        .First(directory => File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))).FullName;
    private static IncomeReplay Replay => new(Enumerable.Range(0, 15).Select(index => new ReplayClear(
        new DateOnly(2026, 10, 1).AddDays(index / 3), new[] { "스우", "루시드", "진 힐라" }[index % 3], 200_000_000 + index * 10_000_000)));
    private static IncomeReplay MesoShowcase => new(Enumerable.Range(0, 100).Select(i => new ReplayClear(
        new DateOnly(2026, 10, 1).AddDays(i / 4), new[] { "스우", "루시드", "진 힐라", "발드릭스" }[i % 4],
        new[] { 550_000_000L, 2_350_000_000, 16_550_000_000, 150_550_000_000 }[i % 4])));
    [Fact]
    public void Meso_milestones_carry_fractional_units_between_records()
    {
        var assets = Path.Combine(Root, "src", "MapleDay", "Assets");
        using var tiers = new IncomeMesoRain(new IncomeReplay([new ReplayClear(new(2026, 10, 1), "스우", 111_100_000_000)]), assets);
        Assert.Equal(111, tiers.DropCount); Assert.Equal(111_000_000_000, tiers.RepresentedMeso);
        using var carry = new IncomeMesoRain(new IncomeReplay([
            new ReplayClear(new(2026, 10, 1), "스우", 600_000_000), new ReplayClear(new(2026, 10, 2), "스우", 600_000_000)]), assets);
        Assert.Equal(1, carry.DropCount); Assert.Equal(1_000_000_000, carry.RepresentedMeso);
    }
    [Fact]
    public void Trillion_meso_heap_grows_over_the_timeline_instead_of_filling_immediately()
    {
        var assets = Path.Combine(Root, "src", "MapleDay", "Assets");
        var replay = new IncomeReplay(Enumerable.Range(0, 100).Select(i =>
            new ReplayClear(new(2026, 10, 1), "스우", 50_000_000_000)));
        using var rain = new IncomeMesoRain(replay, assets);
        Assert.Equal(5_000_000_000_000, rain.RepresentedMeso);
        Assert.Equal(5000, rain.DropCount);
        Assert.Equal(5000, rain.GoldDropCount);
        using var early = new Bitmap(1280, 720); using var settled = new Bitmap(1280, 720);
        using (var graphics = Graphics.FromImage(early)) rain.Draw(graphics, 2);
        using (var graphics = Graphics.FromImage(settled)) rain.Draw(graphics, 10);
        int Filled(Bitmap image) => Enumerable.Range(590, 130)
            .Sum(y => Enumerable.Range(0, 1280).Count(x => image.GetPixel(x, y).A > 0));
        Assert.True(Filled(settled) > Filled(early) * 1.5, "A large total must build the heap progressively.");
    }
    [Fact]
    public void Gold_meso_heap_is_seekable_and_fills_the_floor()
    {
        var assets = Path.Combine(Root, "src", "MapleDay", "Assets");
        var replay = MesoShowcase;
        using var rain = new IncomeMesoRain(replay, assets);
        using var a = new Bitmap(1280, 720); using var b = new Bitmap(1280, 720);
        using (var graphics = Graphics.FromImage(a)) rain.Draw(graphics, 10);
        using (var graphics = Graphics.FromImage(b)) rain.Draw(graphics, 2);
        using (var graphics = Graphics.FromImage(b)) { graphics.Clear(Color.Transparent); rain.Draw(graphics, 10); }
        Assert.Equal(IncomeReplayRenderer.Pixels(a), IncomeReplayRenderer.Pixels(b));
        var bottom = Enumerable.Range(650, 70).SelectMany(y => Enumerable.Range(25, 1230).Select(x => a.GetPixel(x, y).A));
        Assert.True(bottom.Count(alpha => alpha > 0) > 1230 * 70 / 3, "Accumulated sprites must fill the bottom of the frame.");
        var output = Path.Combine(Root, "artifacts", "build", "replay-preview"); Directory.CreateDirectory(output);
        using var renderer = new IncomeReplayRenderer(replay, new("both"), "전체 캐릭터", assets);
        foreach (var seconds in new[] { 0d, 3, 6, 10 })
        {
            using var frame = renderer.Render(seconds);
            frame.Save(Path.Combine(output, $"meso-{seconds:00}.png"), ImageFormat.Png);
        }
    }
    [Fact]
    public void Renderer_writes_preview_frames_with_the_same_currency_settings()
    {
        var output = Path.Combine(Root, "artifacts", "build", "replay-preview"); Directory.CreateDirectory(output);
        using var renderer = new IncomeReplayRenderer(Replay, new("both"), "전체 캐릭터", Path.Combine(Root, "src", "MapleDay", "Assets"));
        foreach (var seconds in new[] { 0d, 3, 6, IncomeReplayRenderer.Duration })
        {
            using var frame = renderer.Render(seconds); Assert.Equal(1920, frame.Width); Assert.Equal(1080, frame.Height);
            frame.Save(Path.Combine(output, $"{seconds:00}.png"), ImageFormat.Png);
        }
        var choices = ManualWeeklyHistory.Choices.GroupBy(price => price.Name).Select(group => group.First()).ToArray();
        foreach (var count in new[] { 12, 24 })
        {
            var clears = choices.Take(count).Select((price, index) => new ReplayClear(new(2026, 10, 1), price.Name, price.Meso));
            using var grid = new IncomeReplayRenderer(new IncomeReplay(clears), new("both"), "전체 캐릭터", Path.Combine(Root, "src", "MapleDay", "Assets"));
            using var sample = grid.Render(10);
            sample.Save(Path.Combine(output, $"cards-{count}.png"), ImageFormat.Png);
        }
    }
    [Fact]
    public async Task Gif_is_animated_with_frame_delays_and_loop_extension()
    {
        var output = Path.Combine(Root, "artifacts", "build", "replay-preview"); Directory.CreateDirectory(output);
        var path = Path.Combine(output, "smoke.gif");
        var full = Environment.GetEnvironmentVariable("MAPLEDAY_EXPORT_QA") == "1";
        await IncomeReplayExport.ExportAsync(full ? MesoShowcase : Replay, new("both"), "전체 캐릭터", Path.Combine(Root, "src", "MapleDay", "Assets"), "", path, true, duration: full ? IncomeReplayRenderer.Duration : .2);
        using var image = Image.FromFile(path);
        Assert.Equal(full ? (int)(IncomeReplayRenderer.Duration * 25) : 5, image.GetFrameCount(FrameDimension.Time)); Assert.Equal(1280, image.Width);
        Assert.Contains("NETSCAPE2.0", System.Text.Encoding.Latin1.GetString(await File.ReadAllBytesAsync(path)));
        Assert.Contains((byte)4, image.GetPropertyItem(0x5100)!.Value!);
    }
    [Fact]
    public async Task Mp4_uses_bundled_windows_encoder_and_writes_a_finalized_container()
    {
        var output = Path.Combine(Root, "artifacts", "build", "replay-preview"); Directory.CreateDirectory(output);
        var path = Path.Combine(output, "smoke.mp4");
        var full = Environment.GetEnvironmentVariable("MAPLEDAY_EXPORT_QA") == "1";
        await IncomeReplayExport.ExportAsync(full ? MesoShowcase : Replay, new("cash", 2000), "전체 캐릭터", Path.Combine(Root, "src", "MapleDay", "Assets"),
            Path.Combine(Root, "artifacts", "MapleDay", "App", "MapleDay.Media.exe"), path, false,
            duration: full ? IncomeReplayRenderer.Duration : .2);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.Length > 1000); var atoms = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.Contains("ftyp", atoms); Assert.Contains("moov", atoms); Assert.Contains("mdat", atoms);
        var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
        var composition = new MediaComposition(); composition.Clips.Add(clip);
        using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.Zero, 1920, 1080, VideoFramePrecision.NearestFrame);
        var decoder = await BitmapDecoder.CreateAsync(thumbnail);
        var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        var brightest = Enumerable.Range(40, 34).SelectMany(y => Enumerable.Range(55, 180).Select(x => pixels[(y * 1920 + x) * 4])).Max();
        Assert.True(brightest > 210, "MAPLEDAY title must appear at the top of the decoded video; reject upside-down frames.");
    }
    [Fact]
    public async Task Cancelled_export_preserves_existing_destination()
    {
        var output = Path.Combine(Root, "artifacts", "build", "replay-preview"); Directory.CreateDirectory(output);
        var path = Path.Combine(output, "cancel.gif"); await File.WriteAllTextAsync(path, "existing");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => IncomeReplayExport.ExportAsync(Replay, new(), "테스트",
            Path.Combine(Root, "src", "MapleDay", "Assets"), "", path, true, cancellationToken: cancellation.Token));
        Assert.Equal("existing", await File.ReadAllTextAsync(path));
    }
}

internal static class TestDirectoryAncestors
{
    internal static IEnumerable<DirectoryInfo> Ancestors(this DirectoryInfo directory)
    { for (var current = directory; current is not null; current = current.Parent) yield return current; }
}
