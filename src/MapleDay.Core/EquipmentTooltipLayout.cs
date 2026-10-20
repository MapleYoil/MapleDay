namespace MapleDay.Core;

public readonly record struct TooltipBox(double X, double Y, double Width, double Height);
public readonly record struct TooltipPlacement(double X, double Y, double Width, double Height, double Scale);

public static class EquipmentTooltipLayout
{
    public const double Width = 324;
    public static (TooltipBox Top, TooltipBox Middle, TooltipBox Bottom) Frame(double height)
    {
        height = Math.Max(42, height);
        return (new(0, 0, Width, 30), new(0, 30, Width, height - 42), new(0, height - 12, Width, 12));
    }

    public static TooltipPlacement Place(double pointerX, double pointerY, double contentHeight, double viewportWidth, double viewportHeight)
    {
        const double margin = 8, gap = 14;
        var availableWidth = Math.Max(1, viewportWidth - margin * 2);
        var availableHeight = Math.Max(1, viewportHeight - margin * 2);
        // Keep original game typography at 1:1. A small viewport scrolls instead of scaling glyphs.
        var width = Math.Min(Width, availableWidth); var height = Math.Min(Math.Max(42, contentHeight), availableHeight);
        var x = pointerX + gap;
        if (x + width > viewportWidth - margin) x = pointerX - gap - width;
        x = Math.Clamp(x, Math.Min(margin, viewportWidth / 2), Math.Max(margin, viewportWidth - margin - width));
        var y = Math.Clamp(pointerY - 16, Math.Min(margin, viewportHeight / 2), Math.Max(margin, viewportHeight - margin - height));
        return new(x, y, width, height, 1);
    }
}
