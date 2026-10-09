using System.Drawing;
using System.Drawing.Drawing2D;
using MapleDay.Core;

namespace MapleDay.Services;

// A fixed timeline keeps preview, arbitrary seeks, MP4 and GIF identical at the same time.
public sealed class IncomeMesoRain : IDisposable
{
    public const long BronzeUnit = 100_000_000;
    private readonly Image[][] _images;
    private readonly List<Drop> _drops = [];
    private readonly List<int> _featured = [];
    private readonly Bitmap _pile = new(1280, 160);
    private readonly Graphics _pileGraphics;
    private int _bakedThrough;
    private double _lastDraw = -1;
    private sealed record Drop(int Kind, double Birth, float X, float Y, float Size, float StartX, float Rotation, float Spin);
    public int DropCount => _drops.Count;
    public long RepresentedMeso { get; private set; }

    public IncomeMesoRain(IncomeReplay replay, string assets)
    {
        _pileGraphics = Graphics.FromImage(_pile);
        _pileGraphics.CompositingQuality = CompositingQuality.HighSpeed;
        _pileGraphics.TranslateTransform(0, -560);
        _images = new[] { "bronze", "gold", "notes", "bag" }.Select(name => Enumerable.Range(0, 4)
            .Select(frame => Image.FromFile(Path.Combine(assets, "IncomeReplay", "Meso", $"{name}-{frame}.png"))).ToArray()).ToArray();
        var random = new Random(731);
        const int columns = 42;
        // Keep a large revenue total from filling the floor in the first few frames.
        // Logical denominations are still counted exactly; visible sprites sample the same timeline.
        var units = replay.Total / BronzeUnit;
        var visibleStride = units <= 1200 ? 1L : (long)Math.Ceiling(units / 720d);
        while (visibleStride % 2 == 0 || visibleStride % 5 == 0) visibleStride++;
        var visibleLimit = units / visibleStride + units / 1000;
        var layerStep = (float)Math.Min(17, 92d / Math.Max(1, Math.Ceiling(visibleLimit / (double)columns)));
        var landingSlots = new List<int>();
        var layer = -1;
        long remainder = 0;
        for (var i = 0; i < replay.Clears.Count; i++)
        {
            var clear = replay.Clears[i];
            var available = clear.Meso + remainder;
            var count = available / BronzeUnit;
            for (long coin = 1; coin <= count; coin++)
            {
                RepresentedMeso += BronzeUnit;
                var milestone = RepresentedMeso / BronzeUnit;
                if (milestone % visibleStride != 0 && milestone % 1000 != 0) continue;
                var kind = milestone % 1000 == 0 ? 3 : milestone % 100 == 0 ? 2 : milestone % 10 == 0 ? 1 : 0;
                var fraction = Math.Clamp((coin * BronzeUnit - remainder) / (double)clear.Meso, 0, 1);
                var birth = .5 + (IncomeReplayRenderer.Duration - 2.5) * (i + fraction) / replay.Clears.Count;
                var size = kind < 2 ? 37f : kind == 2 ? 48f : 50f;
                // Fill a loose, staggered layer before adding another. Rotation and jitter avoid a grid,
                // while complete floor layers prevent unsupported bridges and gaps under the heap.
                if (landingSlots.Count == 0)
                {
                    landingSlots.AddRange(Enumerable.Range(0, columns)); layer++;
                }
                var slotIndex = random.Next(landingSlots.Count);
                var slot = landingSlots[slotIndex]; landingSlots.RemoveAt(slotIndex);
                var x = Math.Clamp(16 + slot * 30 + (layer % 2) * 13 + random.Next(-6, 7), 20, 1260);
                var ceiling = 78 + 23 * (float)Math.Sin(x / 1280 * Math.PI) + random.Next(-5, 6);
                var y = 719 - size / 2 - Math.Min(Math.Max(0, layer * layerStep + random.Next(-3, 4)), ceiling);
                if (kind >= 2) _featured.Add(_drops.Count);
                _drops.Add(new(kind, birth, x, y, size, Math.Clamp(x + random.Next(-85, 86), 25, 1255),
                    random.Next(-22, 23), random.Next(-150, 151)));
            }
            remainder = available % BronzeUnit;
        }
    }

    public void Draw(Graphics graphics, double seconds)
    {
        if (seconds < _lastDraw) { _pileGraphics.Clear(Color.Transparent); _bakedThrough = 0; }
        _lastDraw = seconds;
        var end = UpperBound(seconds);
        // The settled heap is painted once. It no longer needs hundreds of transforms every frame.
        while (_bakedThrough < end && seconds - _drops[_bakedThrough].Birth >= 1.5)
            DrawDrop(_pileGraphics, _drops[_bakedThrough++], 1.5, true);
        var pileState = graphics.Save();
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.DrawImage(_pile, new Rectangle(0, 560, 1280, 160));
        graphics.Restore(pileState);
        var featuredEnd = _featured.BinarySearch(end);
        if (featuredEnd < 0) featuredEnd = ~featuredEnd;
        var indices = new SortedSet<int>();
        var stride = Math.Max(1, (int)Math.Ceiling((end - _bakedThrough) / 300d));
        for (var i = _bakedThrough; i < end; i += stride) indices.Add(i);
        foreach (var i in _featured.Skip(Math.Max(0, featuredEnd - 200)).Take(Math.Min(200, featuredEnd)))
            if (i >= _bakedThrough) indices.Add(i);
        foreach (var i in indices)
            DrawDrop(graphics, _drops[i], seconds - _drops[i].Birth);
    }
    private void DrawDrop(Graphics graphics, Drop drop, double age, bool settled = false)
    {
        const double startY = -60, velocity = 130, gravity = 1900;
        var landing = (-velocity + Math.Sqrt(velocity * velocity + 2 * gravity * (drop.Y - startY))) / gravity;
        var flying = !settled && age < landing;
        var t = Math.Clamp(age / landing, 0, 1);
        var x = drop.StartX + (drop.X - drop.StartX) * t;
        var y = settled ? drop.Y : flying ? startY + velocity * age + .5 * gravity * age * age
            : drop.Y - Math.Abs(Math.Sin((age - landing) * 12)) * 17 * Math.Exp(-(age - landing) * 7);
        var angle = drop.Rotation + (flying ? drop.Spin * (1 - t) : 0);
        var frame = flying ? (int)(age * 9) % 4 : 0;
        var image = _images[drop.Kind][frame];
        var state = graphics.Save();
        graphics.TranslateTransform((float)x, (float)y); graphics.RotateTransform((float)angle);
        graphics.CompositingQuality = CompositingQuality.HighSpeed;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        var scale = drop.Size / Math.Max(_images[drop.Kind][0].Width, _images[drop.Kind][0].Height);
        graphics.DrawImage(image, -image.Width * scale / 2, -image.Height * scale / 2, image.Width * scale, image.Height * scale);
        graphics.Restore(state);
    }
    private int UpperBound(double seconds)
    {
        var lo = 0; var hi = _drops.Count;
        while (lo < hi) { var mid = lo + (hi - lo) / 2; if (_drops[mid].Birth <= seconds) lo = mid + 1; else hi = mid; }
        return lo;
    }
    public void Dispose() { _pileGraphics.Dispose(); _pile.Dispose(); foreach (var frames in _images) foreach (var image in frames) image.Dispose(); }
}
