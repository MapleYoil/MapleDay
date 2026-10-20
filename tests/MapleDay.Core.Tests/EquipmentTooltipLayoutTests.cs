using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class EquipmentTooltipLayoutTests
{
    [Theory]
    [InlineData(42)]
    [InlineData(300)]
    [InlineData(650)]
    [InlineData(1200)]
    public void FrameCoversFullContentHeightWithNoGapOrOverlap(double height)
    {
        var (top, middle, bottom) = EquipmentTooltipLayout.Frame(height);
        Assert.Equal(0, top.Y); Assert.Equal(top.Y + top.Height, middle.Y);
        Assert.Equal(middle.Y + middle.Height, bottom.Y); Assert.Equal(height, bottom.Y + bottom.Height);
        Assert.Equal(height, top.Height + middle.Height + bottom.Height);
        Assert.Equal(324, top.Width); Assert.Equal(top.Width, middle.Width); Assert.Equal(top.Width, bottom.Width);
    }

    [Theory]
    [InlineData(100, 100, 600, 1186, 793)]
    [InlineData(1150, 700, 600, 1186, 793)]
    [InlineData(1100, 30, 1100, 1186, 793)]
    [InlineData(350, 400, 900, 420, 520)]
    [InlineData(100, 100, 900, 280, 400)]
    public void PopupStaysInsideViewportIncludingTallAndNarrowWindows(double x, double y, double contentHeight, double width, double height)
    {
        var result = EquipmentTooltipLayout.Place(x, y, contentHeight, width, height);
        Assert.True(result.X >= 0 && result.Y >= 0);
        Assert.True(result.X + result.Width <= width);
        Assert.True(result.Y + result.Height <= height);
        Assert.Equal(1, result.Scale);
        Assert.Equal(Math.Min(324, width - 16), result.Width, 6);
        Assert.Equal(Math.Min(contentHeight, height - 16), result.Height, 6);
    }

    [Fact]
    public void PopupUsesRightSideWhenItFitsAndLeftSideNearRightEdge()
    {
        var right = EquipmentTooltipLayout.Place(400, 200, 500, 1186, 793);
        Assert.Equal(414, right.X); Assert.Equal(1, right.Scale);
        var left = EquipmentTooltipLayout.Place(1100, 200, 500, 1186, 793);
        Assert.True(left.X + left.Width < 1100); Assert.Equal(1, left.Scale);
    }

    [Fact]
    public void LongTooltipKeepsOriginalWidthAndScaleInsteadOfShrinkingFont()
    {
        var result = EquipmentTooltipLayout.Place(400, 200, 1600, 1186, 793);
        Assert.Equal(324, result.Width); Assert.Equal(777, result.Height); Assert.Equal(1, result.Scale);
    }

    [Fact]
    public void StatComponentsMatchDisplayedTotalWithoutDroppingUnknownContributions()
    {
        var stat = new EquipmentStat("공격력", 662, false, 249, 120, 72, 221, 0);
        Assert.Equal(new[] { (249m, "base"), (221m, "starforce"), (72m, "scroll"), (120m, "bonus") }, stat.Components);
        Assert.Equal(stat.Total, stat.Components.Sum(part => part.Value));
        Assert.Contains((10m, "other"), (stat with { Total = 672 }).Components);
        Assert.Empty((stat with { Base = null }).Components);
    }
}
