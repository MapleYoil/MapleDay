namespace MapleDay.Core;

public static class SchedulerScroll
{
    public const double ThumbHeight = 29;
    public const double RowHeight = 38;
    public const double WheelStep = RowHeight * 2;
    public const double ContinuationWheelStep = RowHeight * 2.5;
    public const double AnimationMilliseconds = 300;
    public const double RestartAnimationMilliseconds = 300;
    public const double MinimumContinuationMilliseconds = 250;
    public static SchedulerWheelMotion PlanWheel(double currentOffset, int delta, double extent, double viewport, bool isAnimating,
        SchedulerWheelAnimation? previous = null)
    {
        var anchor = currentOffset;
        if (isAnimating && previous is { } animation && delta != 0)
        {
            var direction = Math.Sign(-delta);
            // Carry enough of the preceding motion into the new destination.
            // The rendered animation still begins at the actual visible offset.
            if (Math.Sign(animation.Target - animation.From) == direction)
            {
                var progressed = AnimationOffset(animation.From, animation.Target,
                    Math.Max(animation.ElapsedMilliseconds, MinimumContinuationMilliseconds), animation.DurationMilliseconds);
                anchor = direction > 0 ? Math.Max(anchor, progressed) : Math.Min(anchor, progressed);
            }
        }
        return new(WheelOffset(anchor, delta, extent, viewport, isAnimating ? ContinuationWheelStep : WheelStep),
            isAnimating ? RestartAnimationMilliseconds : AnimationMilliseconds);
    }
    public static double AnimationOffset(double from, double target, double elapsedMilliseconds, double durationMilliseconds = AnimationMilliseconds)
    {
        var progress = Math.Clamp(elapsedMilliseconds / durationMilliseconds, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        return from + (target - from) * eased;
    }
    public static double WheelOffset(double offset, int delta, double extent, double viewport, double step = WheelStep) =>
        ClampOffset(offset - delta / 120d * step, extent, viewport);
    public static double DragOffset(double initialOffset, double initialPointerY, double pointerY, double extent, double viewport) =>
        ClampOffset(initialOffset + initialPointerY - pointerY, extent, viewport);
    public static double MaximumOffset(double extent, double viewport) => Math.Max(0, extent - viewport);
    public static double ClampOffset(double offset, double extent, double viewport) => Math.Clamp(offset, 0, MaximumOffset(extent, viewport));
    public static double ThumbTop(double offset, double extent, double viewport)
    {
        var maximum = MaximumOffset(extent, viewport);
        var travel = Math.Max(0, viewport - ThumbHeight - 2);
        return 2 + (maximum > 0 ? Math.Clamp(offset, 0, maximum) / maximum * travel : 0);
    }
    public static double OffsetAtThumb(double top, double extent, double viewport)
    {
        var travel = Math.Max(0, viewport - ThumbHeight - 2);
        return travel > 0 ? Math.Clamp((top - 2) / travel, 0, 1) * MaximumOffset(extent, viewport) : 0;
    }
}

public readonly record struct SchedulerWheelMotion(double Target, double DurationMilliseconds);
public readonly record struct SchedulerWheelAnimation(double From, double Target, double DurationMilliseconds, double ElapsedMilliseconds);
