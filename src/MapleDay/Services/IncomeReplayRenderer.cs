using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using MapleDay.Core;

namespace MapleDay.Services;

// Preview and exports use this same renderer and clock: no screen capture, no scene cuts.
public sealed class IncomeReplayRenderer : IDisposable
{
    public const double Duration = 10;
    public const int PreviewWidth = 1920, PreviewHeight = 1080;
    private readonly IncomeReplay _replay;
    private IncomeDisplay _display;
    private readonly string _scope;
    private readonly Dictionary<string, Image> _icons = [];
    private readonly Dictionary<string, Image> _lootIcons = [];
    private readonly Dictionary<string, (double From, double To, double At)> _positions = [];
    private readonly Dictionary<string, (PointF From, PointF To, double At)> _gridPositions = [];
    private readonly PrivateFontCollection _fonts = new();
    private readonly FontFamily _family;
    private readonly IncomeMesoRain _mesoRain;
    private double _lastTime = -1;
    private static readonly Color White = Color.FromArgb(241, 245, 252), Muted = Color.FromArgb(153, 168, 190), Blue = Color.FromArgb(110, 174, 255);
    public IncomeReplayRenderer(IncomeReplay replay, IncomeDisplay display, string scope, string assets)
    {
        _replay = replay; _display = display; _scope = scope;
        var font = Path.Combine(assets, "Fonts", "PretendardVariable.ttf");
        if (!File.Exists(font)) throw new FileNotFoundException("수익 집계에 필요한 Pretendard 글꼴 파일을 찾지 못했어요.", font);
        _fonts.AddFontFile(font);
        _family = _fonts.Families.FirstOrDefault() ?? throw new InvalidDataException("Pretendard 글꼴을 읽지 못했어요.");
        _mesoRain = new(replay, assets);
        foreach (var name in replay.Names)
        {
            if (SchedulerIconAssets.BossFile(name) is not { } file) continue;
            var path = Path.Combine(assets, "Scheduler", file.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) _icons[name] = Image.FromFile(path);
        }
        foreach (var item in replay.Clears.SelectMany(clear => clear.Loot ?? []).DistinctBy(item => item.Icon))
        {
            if (string.IsNullOrEmpty(item.Icon)) continue;
            var path = Path.Combine(assets, item.Icon.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) _lootIcons[item.Icon] = Image.FromFile(path);
        }
    }
    public void SetDisplay(IncomeDisplay display) => _display = display;
    public Bitmap Render(double seconds, int width = PreviewWidth, int height = PreviewHeight, bool stableRanking = false)
    {
        var progress = Math.Clamp((seconds - .5) / (Duration - 2.5), 0, 1);
        var frame = _replay.At(progress);
        var orderedBosses = stableRanking ? _replay.Names.Select(name => frame.Bosses.First(row => row.Name == name)).ToArray() : frame.Bosses;
        if (seconds < _lastTime) { _positions.Clear(); _gridPositions.Clear(); }
        _lastTime = seconds;
        var count = frame.Bosses.Count;
        var columns = count <= 4 ? 2 : count <= 9 ? 3 : count <= 16 ? 4 : 6;
        for (var rank = 0; rank < orderedBosses.Count; rank++)
        {
            var name = orderedBosses[rank].Name;
            if (!_positions.TryGetValue(name, out var position)) _positions[name] = (rank, rank, seconds);
            else if (position.To != rank) _positions[name] = (Position(position, seconds), rank, seconds);
            var target = new PointF(rank % columns, rank / columns);
            if (!_gridPositions.TryGetValue(name, out var cell)) _gridPositions[name] = (target, target, seconds);
            else if (cell.To != target) _gridPositions[name] = (GridPosition(cell, seconds), target, seconds);
        }
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.ScaleTransform(width / 1280f, height / 720f);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.FromArgb(10, 15, 25));
        using (var glow = new LinearGradientBrush(new Rectangle(0, 0, 1280, 720), Color.FromArgb(18, 36, 64), Color.FromArgb(8, 13, 23), 35f))
            g.FillRectangle(glow, 0, 0, 1280, 720);
        _mesoRain.Draw(g, seconds);
        // Keep the values readable while the game sprites rain behind the content.
        using (var veil = new SolidBrush(Color.FromArgb(225, 14, 23, 38)))
        {
            g.FillRoundedRectangle(veil, new RectangleF(24, 18, 440, 126), 12);
            g.FillRoundedRectangle(veil, new RectangleF(24, 218, 440, 470), 12);
        }
        Text(g, "MAPLEDAY", 36, 27, 18, Blue, FontStyle.Bold);
        Text(g, "보스 · 물욕템 수익", 36, 64, 32, White, FontStyle.Bold);
        Text(g, _scope, 36, 110, 18, Muted);
        Text(g, frame.Date?.ToString("yyyy.MM.dd") ?? "저장된 기록 없음", 36, 230, 20, Muted);
        var main = _display.ValidMode == "cash" ? _display.CashText(frame.Total) : IncomeReplay.CompactMeso(frame.Total);
        Text(g, "누적 " + (_display.ValidMode == "cash" ? "환산 금액" : "메소"), 36, 267, 20, Muted);
        var pulse = (float)Math.Exp(-(progress * _replay.Clears.Count % 1) * 9);
        using (var glowShape = new GraphicsPath())
        {
            glowShape.AddEllipse(24, 295, 430, 102);
            using var glowBrush = new PathGradientBrush(glowShape) { CenterColor = Color.FromArgb((int)(15 + pulse * 25), 91, 164, 255), SurroundColors = [Color.Transparent] };
            g.FillPath(glowBrush, glowShape);
        }
        var moneyState = g.Save();
        g.TranslateTransform(36, 337); g.ScaleTransform(1 + pulse * .025f, 1 + pulse * .025f); g.TranslateTransform(-36, -337);
        var mainSize = _display.ValidMode == "cash" ? main.Length > 19 ? 30 : main.Length > 14 ? 36 : 52 : main.Length > 12 ? 44 : main.Length > 9 ? 56 : 68;
        Text(g, main, 32, 301, mainSize, frame.Total >= 1_000_000_000_000 ? Blue : White, FontStyle.Bold);
        g.Restore(moneyState);
        if (_display.ValidMode == "both") Text(g, _display.CashText(frame.Total), 36, 398, 32, Blue, FontStyle.Bold);
        Text(g, $"주간 보스 {frame.Count:N0}마리", 36, 170, 22, White);
        using (var track = new SolidBrush(Color.FromArgb(38, 51, 71))) g.FillRectangle(track, 36, 676, 408, 3);
        using (var fill = new SolidBrush(Blue)) g.FillRectangle(fill, 36, 676, (float)(408 * progress), 3);
        if (frame.CollectedLoot is { Count: > 0 } loot)
        {
            Text(g, $"누적 물욕템 · {loot.Count}종", 36, 438, 17, White, FontStyle.Bold);
            var lootColumns = loot.Count <= 12 ? 2 : loot.Count <= 24 ? 3 : 4;
            var lootRows = (int)Math.Ceiling(loot.Count / (double)lootColumns);
            var rowHeight = Math.Min(34f, 204f / lootRows);
            var cellWidth = 408f / lootColumns;
            for (var i = 0; i < loot.Count; i++)
            {
                var item = loot[i]; var x = 36 + (i % lootColumns) * cellWidth; var y = 467 + (i / lootColumns) * rowHeight;
                var iconSize = Math.Min(28, rowHeight - 3);
                if (_lootIcons.TryGetValue(item.Icon, out var image))
                {
                    var iconState = g.Save(); g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(image, x, y + (rowHeight - iconSize) / 2, iconSize, iconSize); g.Restore(iconState);
                }
                CenterText(g, item.Name, new RectangleF(x + iconSize + 3, y, cellWidth - iconSize - 7, rowHeight * .53f), Math.Min(11, rowHeight * .42f), White, true);
                CenterText(g, $"{item.Count}개 · +{IncomeReplay.CompactMeso(item.Meso)}", new RectangleF(x + iconSize + 3, y + rowHeight * .5f, cellWidth - iconSize - 7, rowHeight * .5f), Math.Min(10, rowHeight * .4f), Blue, true);
            }
        }
        else Text(g, "결정석 수익 집계", 36, 468, 18, Muted);
        var rows = Math.Max(1, (int)Math.Ceiling(count / (double)columns));
        const float gap = 12;
        var side = Math.Min(230f, Math.Min((750 - gap * (columns - 1)) / columns, (480 - gap * (rows - 1)) / rows));
        var left = 492 + (750 - (side * columns + gap * (columns - 1))) / 2;
        foreach (var row in frame.Bosses.OrderBy(row => Position(_positions[row.Name], seconds)))
        {
            var cell = GridPosition(_gridPositions[row.Name], seconds);
            var x = left + cell.X * (side + gap);
            var y = 86 + cell.Y * (side + gap);
            using var background = new SolidBrush(Color.FromArgb(33, 45, 64));
            g.FillRoundedRectangle(background, new RectangleF(x, y, side, side), Math.Min(24, side * .13f));
            var both = _display.ValidMode == "both";
            var compact = side < 130;
            var headingSize = compact ? 13 : Math.Clamp(side * .112f, 13, 23);
            var amountSize = compact ? 14 : Math.Clamp(side * .115f, 13, 25);
            var smallSize = compact ? 11 : Math.Clamp(side * .09f, 12, 18);
            Text(g, $"{row.Count:N0}마리", x + side - 10, y + (compact ? 5 : 9), smallSize, Muted, right: true);
            if (_icons.TryGetValue(row.Name, out var icon))
            {
                var size = compact ? 20 : Math.Clamp(side * .24f, 24, 56);
                var iconTop = compact ? 20 : side * .17f;
                using var iconBackground = new SolidBrush(Color.FromArgb(19, 29, 46));
                g.FillRoundedRectangle(iconBackground, new RectangleF(x + (side - size - 10) / 2, y + iconTop - 5, size + 10, size + 10), 12);
                var state = g.Save(); g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(icon, x + (side - size) / 2, y + iconTop, size, size); g.Restore(state);
            }
            CenterText(g, row.Name, new RectangleF(x + 6, y + (compact ? 43 : side * .46f), side - 12, compact ? 31 : side * .25f), headingSize, White);
            var value = _display.ValidMode == "cash" ? _display.CashText(row.Meso) : (row.Meso / 100_000_000m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "억";
            CenterText(g, value, new RectangleF(x + 6, y + (compact ? 74 : side * (both ? .72f : .77f)), side - 12, compact ? 19 : side * .16f), amountSize, Blue, true);
            if (both) CenterText(g, _display.CashText(row.Meso), new RectangleF(x + 6, y + (compact ? 94 : side * .87f), side - 12, compact ? 14 : side * .12f), smallSize, Muted, true);
        }
        if (frame.Bosses.Count == 0) Text(g, "집계할 주간 보스 기록이 없습니다.", 570, 335, 22, Muted);
        Text(g, "보스별 누적 수익 순", 494, 44, 19, Muted);
        return bitmap;
    }
    private static double Position((double From, double To, double At) value, double seconds)
    { var t = Math.Clamp((seconds - value.At) / .45, 0, 1); t = 1 - Math.Pow(1 - t, 3); return value.From + (value.To - value.From) * t; }
    private static PointF GridPosition((PointF From, PointF To, double At) value, double seconds)
    {
        var t = (float)(1 - Math.Pow(1 - Math.Clamp((seconds - value.At) / .45, 0, 1), 3));
        return new(value.From.X + (value.To.X - value.From.X) * t, value.From.Y + (value.To.Y - value.From.Y) * t);
    }
    private void Text(Graphics g, string text, float x, float y, float size, Color color, FontStyle style = FontStyle.Regular, bool right = false)
    {
        using var font = new Font(_family, size, style, GraphicsUnit.Pixel); using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = right ? StringAlignment.Far : StringAlignment.Near };
        g.DrawString(text, font, brush, x, y, format);
    }
    private void CenterText(Graphics g, string text, RectangleF rect, float size, Color color, bool singleLine = false)
    {
        using var font = new Font(_family, size, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter, FormatFlags = singleLine ? StringFormatFlags.NoWrap : 0 };
        g.DrawString(text, font, brush, rect, format);
    }
    public static byte[] Pixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * bitmap.Width * 4, bitmap.Width * 4);
            return bytes;
        }
        finally { bitmap.UnlockBits(data); }
    }
    public void Dispose() { _mesoRain.Dispose(); foreach (var icon in _icons.Values) icon.Dispose(); foreach (var icon in _lootIcons.Values) icon.Dispose(); _fonts.Dispose(); }
}

internal static class ReplayDrawing
{
    internal static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath(); var diameter = Math.Min(radius * 2, rect.Height);
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90); path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); g.FillPath(brush, path);
    }
}
