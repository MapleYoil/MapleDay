using System.Drawing;
using System.Drawing.Drawing2D;
using MapleDay.Core;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace MapleDay.Services;

// A fixed timeline keeps preview, arbitrary seeks, MP4 and GIF identical at the same time.
public sealed class IncomeMesoRain : IDisposable
{
    public const long GoldUnit = 1_000_000_000;
    private readonly List<SKBitmap[]> _images = [];
    private readonly List<Drop> _drops = [];
    private readonly SKBitmap _pile = new(new SKImageInfo(1280, 160, SKColorType.Bgra8888, SKAlphaType.Premul));
    private readonly SKCanvas _pileGraphics;
    private int _bakedThrough;
    private double _lastDraw = -1;
    private sealed record Drop(int Kind, double Birth, float X, float Y, float Size, float StartX, float Rotation, float Spin);
    public int DropCount => _drops.Count;
    public int GoldDropCount => _drops.Count(drop => drop.Kind == 0);
    public int LootDropCount => _drops.Count(drop => drop.Kind != 0);
    public long RepresentedMeso { get; private set; }

    public IncomeMesoRain(IncomeReplay replay, string assets)
    {
        _pile.Erase(SKColors.Transparent);
        _pileGraphics = new SKCanvas(_pile);
        _pileGraphics.Translate(0, -560);
        _images.Add(Enumerable.Range(0, 4)
            .Select(frame => SKBitmap.Decode(Path.Combine(assets, "IncomeReplay", "Meso", $"gold-{frame}.png"))).ToArray());
        var lootKinds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in replay.Clears.SelectMany(clear => clear.Loot ?? []).DistinctBy(item => item.Icon))
        {
            if (string.IsNullOrEmpty(item.Icon)) continue;
            var path = Path.Combine(assets, item.Icon.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            lootKinds[item.Icon] = _images.Count; _images.Add([SKBitmap.Decode(path)]);
        }
        var random = new Random(731);
        const int columns = 42;
        // Every 1 billion crystal mesos becomes a gold coin; every reward uses its own icon.
        long CrystalMeso(ReplayClear clear) => Math.Max(0, clear.Meso - (clear.Loot?.Sum(item => item.Meso) ?? 0));
        var units = replay.Clears.Sum(CrystalMeso) / GoldUnit;
        var lootCount = replay.Clears.Sum(clear => clear.Loot?.Sum(item => Math.Max(0, item.Count)) ?? 0);
        var visibleLimit = units + lootCount;
        var layerStep = (float)Math.Min(17, 92d / Math.Max(1, Math.Ceiling(visibleLimit / (double)columns)));
        var landingSlots = new List<int>();
        var layer = -1;
        void AddDrop(int kind, double birth, float size)
        {
            if (landingSlots.Count == 0) { landingSlots.AddRange(Enumerable.Range(0, columns)); layer++; }
            var slotIndex = random.Next(landingSlots.Count);
            var slot = landingSlots[slotIndex]; landingSlots.RemoveAt(slotIndex);
            var x = Math.Clamp(16 + slot * 30 + (layer % 2) * 13 + random.Next(-6, 7), 20, 1260);
            var ceiling = 78 + 23 * (float)Math.Sin(x / 1280 * Math.PI) + random.Next(-5, 6);
            var y = 719 - size / 2 - Math.Min(Math.Max(0, layer * layerStep + random.Next(-3, 4)), ceiling);
            _drops.Add(new(kind, birth, x, y, size, Math.Clamp(x + random.Next(-85, 86), 25, 1255),
                random.Next(-22, 23), random.Next(-150, 151)));
        }
        long remainder = 0;
        for (var i = 0; i < replay.Clears.Count; i++)
        {
            var clear = replay.Clears[i];
            var crystal = CrystalMeso(clear);
            var available = crystal + remainder;
            var count = available / GoldUnit;
            for (long coin = 1; coin <= count; coin++)
            {
                var fraction = Math.Clamp((coin * GoldUnit - remainder) / (double)crystal, 0, 1);
                var birth = .5 + (IncomeReplayRenderer.Duration - 2.5) * (i + fraction) / replay.Clears.Count;
                AddDrop(0, birth, 37f);
            }
            RepresentedMeso += count * GoldUnit;
            remainder = available % GoldUnit;
            if (clear.Loot is { Count: > 0 } loot)
                for (var reward = 0; reward < loot.Count; reward++)
                    if (lootKinds.TryGetValue(loot[reward].Icon, out var kind))
                        for (var copy = 0; copy < loot[reward].Count; copy++)
                            AddDrop(kind, .5 + (IncomeReplayRenderer.Duration - 2.5) * (i + .1 + .8 * (reward + (copy + 1d) / Math.Max(1, loot[reward].Count)) / loot.Count) / replay.Clears.Count,
                                replay.Category == "hunting" ? 20f : 43f);
        }
        _drops.Sort((a, b) => a.Birth.CompareTo(b.Birth));
    }

    public void Draw(Graphics graphics, double seconds)
    {
        using var surface = new SKBitmap(new SKImageInfo(1280, 720, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        surface.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(surface)) Draw(canvas, seconds);
        using var bitmap = new Bitmap(1280, 720, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new(0, 0, 1280, 720), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try { var bytes = new byte[1280 * 720 * 4]; Marshal.Copy(surface.GetPixels(), bytes, 0, bytes.Length); Marshal.Copy(bytes, 0, data.Scan0, bytes.Length); }
        finally { bitmap.UnlockBits(data); }
        graphics.DrawImageUnscaled(bitmap, 0, 0);
    }
    internal void Draw(ReplayGraphics graphics, double seconds) => Draw(graphics.Canvas, seconds);
    private void Draw(SKCanvas graphics, double seconds)
    {
        if (seconds < _lastDraw) { _pileGraphics.Clear(SKColors.Transparent); _bakedThrough = 0; }
        _lastDraw = seconds;
        var end = UpperBound(seconds);
        // The settled heap is painted once. It no longer needs hundreds of transforms every frame.
        while (_bakedThrough < end && seconds - _drops[_bakedThrough].Birth >= 1.5)
            DrawDrop(_pileGraphics, _drops[_bakedThrough++], 1.5, true);
        graphics.DrawBitmap(_pile, new SKRect(0, 560, 1280, 720));
        for (var i = _bakedThrough; i < end; i++)
            DrawDrop(graphics, _drops[i], seconds - _drops[i].Birth);
    }
    private void DrawDrop(SKCanvas graphics, Drop drop, double age, bool settled = false)
    {
        const double startY = -60, velocity = 130, gravity = 1900;
        var landing = (-velocity + Math.Sqrt(velocity * velocity + 2 * gravity * (drop.Y - startY))) / gravity;
        var flying = !settled && age < landing;
        var t = Math.Clamp(age / landing, 0, 1);
        var x = drop.StartX + (drop.X - drop.StartX) * t;
        var y = settled ? drop.Y : flying ? startY + velocity * age + .5 * gravity * age * age
            : drop.Y - Math.Abs(Math.Sin((age - landing) * 12)) * 17 * Math.Exp(-(age - landing) * 7);
        var angle = drop.Rotation + (flying ? drop.Spin * (1 - t) : 0);
        var frame = flying ? (int)(age * 9) % _images[drop.Kind].Length : 0;
        var image = _images[drop.Kind][frame];
        var state = graphics.Save();
        graphics.Translate((float)x, (float)y); graphics.RotateDegrees((float)angle);
        var scale = drop.Size / Math.Max(_images[drop.Kind][0].Width, _images[drop.Kind][0].Height);
        graphics.DrawBitmap(image, new SKRect(-image.Width * scale / 2, -image.Height * scale / 2, image.Width * scale / 2, image.Height * scale / 2));
        graphics.RestoreToCount(state);
    }
    private int UpperBound(double seconds)
    {
        var lo = 0; var hi = _drops.Count;
        while (lo < hi) { var mid = lo + (hi - lo) / 2; if (_drops[mid].Birth <= seconds) lo = mid + 1; else hi = mid; }
        return lo;
    }
    public void Dispose() { _pileGraphics.Dispose(); _pile.Dispose(); foreach (var frames in _images) foreach (var image in frames) image.Dispose(); }
}
