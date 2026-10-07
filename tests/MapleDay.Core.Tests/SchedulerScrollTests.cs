using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SchedulerScrollTests
{
    [Fact]
    public void EachWheelNotchMovesExactlyTwoRowsAndClampsAtTheEnds()
    {
        Assert.Equal(76, SchedulerScroll.WheelOffset(0, -120, 1200, 227));
        Assert.Equal(0, SchedulerScroll.WheelOffset(76, 120, 1200, 227));
        Assert.Equal(973, SchedulerScroll.WheelOffset(950, -120, 1200, 227));
        Assert.Equal(0, SchedulerScroll.WheelOffset(10, 120, 1200, 227));
    }
    [Fact]
    public void WheelAnimationReachesTwoRowsIn300MillisecondsWithoutOvershoot()
    {
        var motion = SchedulerScroll.PlanWheel(0, -120, 1200, 227, false);
        var target = motion.Target;
        Assert.Equal(300, motion.DurationMilliseconds);
        Assert.Equal(0, SchedulerScroll.AnimationOffset(0, target, 0));
        Assert.InRange(SchedulerScroll.AnimationOffset(0, target, 150), 38, 76);
        Assert.Equal(target, SchedulerScroll.AnimationOffset(0, target, 300));
        Assert.Equal(target, SchedulerScroll.AnimationOffset(0, target, 400));
    }
    [Fact]
    public void ContinuationWithoutPreviousAnimationFallsBackToVisiblePositionAndRestartsIn300Milliseconds()
    {
        var first = SchedulerScroll.PlanWheel(0, -120, 1200, 227, false);
        var current = SchedulerScroll.AnimationOffset(0, first.Target, 100);
        var second = SchedulerScroll.PlanWheel(current, -120, 1200, 227, true);
        Assert.Equal(current + 95, second.Target);
        Assert.True(second.Target < first.Target + 95);
        Assert.Equal(300, second.DurationMilliseconds);
        Assert.Equal(current, SchedulerScroll.AnimationOffset(current, second.Target, 0, second.DurationMilliseconds));
        Assert.Equal(second.Target, SchedulerScroll.AnimationOffset(current, second.Target, 300, second.DurationMilliseconds));
        var reversed = SchedulerScroll.PlanWheel(current, 120, 1200, 227, true);
        Assert.Equal(0, reversed.Target);
        Assert.Equal(300, reversed.DurationMilliseconds);
        Assert.Equal(973, SchedulerScroll.PlanWheel(950, -120, 1200, 227, true).Target);
        Assert.Equal(300, SchedulerScroll.PlanWheel(second.Target, -120, 1200, 227, false).DurationMilliseconds);
    }
    [Fact]
    public void RapidContinuationCarriesAtLeast250MillisecondsOfPreviousMotionWithoutJumpingItsStart()
    {
        var first = SchedulerScroll.PlanWheel(0, -120, 1200, 227, false);
        var current = SchedulerScroll.AnimationOffset(0, first.Target, 20);
        var previous = new SchedulerWheelAnimation(0, first.Target, first.DurationMilliseconds, 20);
        var second = SchedulerScroll.PlanWheel(current, -120, 1200, 227, true, previous);
        var anchor = SchedulerScroll.AnimationOffset(0, first.Target, 250);
        Assert.Equal(anchor + 95, second.Target, 8);
        Assert.True(second.Target > current + 95);
        Assert.True(second.Target < first.Target + 95);
        Assert.Equal(300, second.DurationMilliseconds);
        Assert.Equal(current, SchedulerScroll.AnimationOffset(current, second.Target, 0, second.DurationMilliseconds));
        Assert.Equal(second.Target, SchedulerScroll.AnimationOffset(current, second.Target, 300, second.DurationMilliseconds));
    }
    [Fact]
    public void ContinuationUsesRealProgressWhenFurtherAlongAndReversalStartsFromVisibleOffset()
    {
        var current = SchedulerScroll.AnimationOffset(200, 276, 280);
        var previous = new SchedulerWheelAnimation(200, 276, 300, 280);
        Assert.Equal(current + 95, SchedulerScroll.PlanWheel(current, -120, 1200, 227, true, previous).Target, 8);
        Assert.Equal(current - 95, SchedulerScroll.PlanWheel(current, 120, 1200, 227, true, previous).Target, 8);
        // Upward input carries motion in the same way and remains bounded.
        previous = new(200, 124, 300, 20);
        current = SchedulerScroll.AnimationOffset(200, 124, 20);
        Assert.Equal(SchedulerScroll.AnimationOffset(200, 124, 250) - 95,
            SchedulerScroll.PlanWheel(current, 120, 1200, 227, true, previous).Target, 8);
        Assert.Equal(973, SchedulerScroll.PlanWheel(950, -120, 1200, 227, true, new(900, 973, 300, 10)).Target);
        Assert.Equal(0, SchedulerScroll.PlanWheel(25, 120, 1200, 227, true, new(76, 0, 300, 10)).Target);
        // A continued 200ms animation has already reached its end at the minimum carry time.
        Assert.Equal(295, SchedulerScroll.PlanWheel(150, -120, 1200, 227, true, new(124, 200, 200, 10)).Target);
    }
    [Fact]
    public void ContentDragKeepsGrabbedPointUnderPointerWithOneToOneMovement()
    {
        const double initialOffset = 200;
        const double grabbedY = 100;
        foreach (var pointerY in new[] { 20d, 73.5, 100, 165, 205 })
        {
            var offset = SchedulerScroll.DragOffset(initialOffset, grabbedY, pointerY, 1200, 227);
            Assert.Equal(initialOffset + grabbedY, offset + pointerY);
        }
        // Dragging out of the viewport can reach either end without overshooting.
        Assert.Equal(0, SchedulerScroll.DragOffset(initialOffset, grabbedY, 500, 1200, 227));
        Assert.Equal(973, SchedulerScroll.DragOffset(initialOffset, grabbedY, -1000, 1200, 227));
        Assert.Equal(0, SchedulerScroll.DragOffset(0, grabbedY, -100, 38, 227));
    }
    [Theory]
    [InlineData(227)]
    [InlineData(493)]
    public void FixedGameThumbCanReachBothEndsWithoutLeavingTheViewport(double viewport)
    {
        var extent = 1200d;
        var end = SchedulerScroll.MaximumOffset(extent, viewport);
        Assert.Equal(2, SchedulerScroll.ThumbTop(0, extent, viewport));
        Assert.Equal(viewport - SchedulerScroll.ThumbHeight, SchedulerScroll.ThumbTop(end, extent, viewport));
        foreach (var offset in new[] { 0, 38d, 76d, end / 2, end })
            Assert.Equal(offset, SchedulerScroll.OffsetAtThumb(SchedulerScroll.ThumbTop(offset, extent, viewport), extent, viewport), 8);
        Assert.Equal(0, SchedulerScroll.ClampOffset(-76, extent, viewport));
        Assert.Equal(end, SchedulerScroll.ClampOffset(extent, extent, viewport));
    }
    [Fact]
    public void EmptyAndShortListsCannotScrollOrDivideByZero()
    {
        Assert.Equal(0, SchedulerScroll.OffsetAtThumb(100, 0, 0));
        Assert.Equal(0, SchedulerScroll.OffsetAtThumb(100, 38, 227));
        Assert.Equal(0, SchedulerScroll.ClampOffset(76, 38, 227));
    }
}
