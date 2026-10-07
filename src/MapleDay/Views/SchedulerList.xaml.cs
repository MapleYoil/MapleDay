using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Numerics;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Composition;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Input;

namespace MapleDay.Views;

public sealed partial class SchedulerList : UserControl
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows), typeof(IEnumerable), typeof(SchedulerList), new PropertyMetadata(null, RowsChanged));
    public static readonly DependencyProperty RowTemplateProperty = DependencyProperty.Register(nameof(RowTemplate), typeof(DataTemplate), typeof(SchedulerList), new PropertyMetadata(null));
    public IEnumerable? Rows { get => (IEnumerable?)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public DataTemplate? RowTemplate { get => (DataTemplate?)GetValue(RowTemplateProperty); set => SetValue(RowTemplateProperty, value); }
    private double _dragTop;
    private bool _dragging;
    private bool _hover;
    private INotifyCollectionChanged? _observedRows;
    private int _animatedScrollId = -1;
    private SchedulerWheelAnimation? _wheelAnimation;
    private long _wheelStartedAt;
    private readonly Dictionary<int, double> _scrollDurations = [];
    private ExpressionAnimation? _thumbAnimation;
    private Visual? _thumbVisual;
    private readonly RectangleGeometry _trackClip = new();
    private static readonly ScrollingScrollOptions ImmediateScroll = new(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore);
    private static readonly ScrollingScrollOptions AnimatedScroll = new(ScrollingAnimationMode.Enabled, ScrollingSnapPointsMode.Ignore);
    private Pointer? _contentPointer;
    private double _contentStartY;
    private double _contentStartOffset;
    private bool _contentDragging;

    public SchedulerList()
    {
        InitializeComponent();
        TrackCanvas.Clip = _trackClip;
        AddHandler(PointerWheelChangedEvent, new PointerEventHandler(Scroller_PointerWheelChanged), true);
        // Also observe handled button presses; dragging a button cancels its click.
        Scroller.AddHandler(PointerPressedEvent, new PointerEventHandler(Scroller_PointerPressed), true);
        Scroller.AddHandler(PointerMovedEvent, new PointerEventHandler(Scroller_PointerMoved), true);
        Scroller.AddHandler(PointerReleasedEvent, new PointerEventHandler(Scroller_PointerReleased), true);
        Scroller.AddHandler(PointerCanceledEvent, new PointerEventHandler(Scroller_PointerCanceled), true);
        Scroller.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Scroller_PointerCaptureLost), true);
        Loaded += (_, _) => { ObserveRows(); StartThumbAnimation(); UpdateScrollbar(); };
        Unloaded += (_, _) => { StopScrollAnimation(); StopThumbAnimation(); EndContentDrag(); if (_observedRows is not null) _observedRows.CollectionChanged -= Rows_CollectionChanged; _observedRows = null; };
        SetThumbImage("normal");
    }
    private static void RowsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var list = (SchedulerList)sender;
        list.StopScrollAnimation();
        list.EndContentDrag();
        list.ObserveRows();
        list.DispatcherQueue.TryEnqueue(list.UpdateScrollbar);
    }
    private void ObserveRows()
    {
        if (_observedRows is not null) _observedRows.CollectionChanged -= Rows_CollectionChanged;
        _observedRows = Rows as INotifyCollectionChanged;
        if (_observedRows is not null) _observedRows.CollectionChanged += Rows_CollectionChanged;
    }
    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => DispatcherQueue.TryEnqueue(() => { StopScrollAnimation(); EndContentDrag(); UpdateScrollbar(); });
    private void UpdateScrollbar()
    {
        if (Scroller is null || ScrollThumb is null) return;
        var viewport = Scroller.ViewportHeight;
        var extent = Scroller.ExtentHeight;
        ScrollThumb.Visibility = SchedulerScroll.MaximumOffset(extent, viewport) > 0 ? Visibility.Visible : Visibility.Collapsed;
        TrackImage.Height = Math.Max(0, ActualHeight);
        _trackClip.Rect = new Rect(0, 0, 8, Math.Max(0, ActualHeight));
    }
    private void StartThumbAnimation()
    {
        StopThumbAnimation();
        ElementCompositionPreview.SetIsTranslationEnabled(ScrollThumb, true);
        _thumbVisual = ElementCompositionPreview.GetElementVisual(ScrollThumb);
        _thumbAnimation = _thumbVisual.Compositor.CreateExpressionAnimation(
            "Clamp((scroll.Position.Y - scroll.MinPosition.Y) / Max(1.0, scroll.MaxPosition.Y - scroll.MinPosition.Y), 0.0, 1.0) * Max(0.0, scroll.Viewport.Y - thumbHeight - 2.0)");
        _thumbAnimation.SetReferenceParameter("scroll", Scroller.ExpressionAnimationSources);
        _thumbAnimation.SetScalarParameter("thumbHeight", (float)SchedulerScroll.ThumbHeight);
        _thumbVisual.StartAnimation("Translation.Y", _thumbAnimation);
    }
    private void StopThumbAnimation()
    {
        _thumbVisual?.StopAnimation("Translation.Y");
        _thumbAnimation?.Dispose();
        _thumbAnimation = null;
        _thumbVisual = null;
    }
    private void SetThumbImage(string state) => ScrollThumb.Background = new ImageBrush
    {
        ImageSource = LocalImageCache.Get($"Scheduler/Scrollbar/thumb_{state}.png"),
        Stretch = Stretch.None,
        AlignmentX = AlignmentX.Left
    };
    private void StopScrollAnimation()
    {
        _wheelAnimation = null;
        if (_animatedScrollId < 0) return;
        _animatedScrollId = -1;
        // An immediate request cancels the native animation at its current position.
        SetScrollOffset(Scroller.VerticalOffset);
    }
    private void ScrollTo(double offset)
    {
        StopScrollAnimation();
        EndContentDrag();
        SetScrollOffset(offset);
    }
    private void SetScrollOffset(double offset) => Scroller.ScrollTo(0, SchedulerScroll.ClampOffset(offset, Scroller.ExtentHeight, Scroller.ViewportHeight), ImmediateScroll);
    private void AnimateScrollTo(SchedulerWheelMotion motion)
    {
        var from = Scroller.VerticalOffset;
        _animatedScrollId = Scroller.ScrollTo(0, motion.Target, AnimatedScroll);
        _wheelAnimation = _animatedScrollId >= 0 ? new(from, motion.Target, motion.DurationMilliseconds, 0) : null;
        _wheelStartedAt = Stopwatch.GetTimestamp();
        if (_animatedScrollId >= 0) _scrollDurations[_animatedScrollId] = motion.DurationMilliseconds;
    }
    private void Scroller_ScrollAnimationStarting(ScrollPresenter sender, ScrollingScrollAnimationStartingEventArgs args)
    {
        // InteractionTracker runs this on the compositor, without a UI timer or
        // per-frame layout/ChangeView calls. Rendering follows the system display clock.
        var compositor = args.Animation.Compositor;
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(_scrollDurations.GetValueOrDefault(args.CorrelationId, SchedulerScroll.AnimationMilliseconds));
        animation.InsertKeyFrame(0, new Vector3(args.StartPosition, 0));
        animation.InsertKeyFrame(1, new Vector3(args.EndPosition, 0),
            compositor.CreateCubicBezierEasingFunction(new Vector2(1f / 3, 1), new Vector2(2f / 3, 1)));
        args.Animation = animation;
        if (args.CorrelationId == _animatedScrollId && _wheelAnimation is { } wheel)
        {
            _wheelAnimation = wheel with { From = Scroller.VerticalOffset };
            _wheelStartedAt = Stopwatch.GetTimestamp();
        }
    }
    private void Scroller_ScrollCompleted(ScrollPresenter sender, ScrollingScrollCompletedEventArgs args)
    {
        _scrollDurations.Remove(args.CorrelationId);
        if (args.CorrelationId == _animatedScrollId) { _animatedScrollId = -1; _wheelAnimation = null; }
    }
    private void Scroller_ExtentChanged(ScrollPresenter sender, object args) => UpdateScrollbar();
    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateScrollbar();
    private void List_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateScrollbar();
    private void Scroller_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(Scroller);
        if (_contentPointer is not null || !point.IsInContact
            || (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed)) return;
        StopScrollAnimation();
        // Leave button focus/capture intact until this actually becomes a drag.
        if (SchedulerScroll.MaximumOffset(Scroller.ExtentHeight, Scroller.ViewportHeight) <= 0) return;
        _contentPointer = args.Pointer;
        _contentStartY = point.Position.Y;
        _contentStartOffset = Scroller.VerticalOffset;
    }
    private void Scroller_PointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_contentPointer?.PointerId != args.Pointer.PointerId) return;
        var point = args.GetCurrentPoint(Scroller);
        if (!point.IsInContact
            || (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed))
        {
            EndContentDrag();
            return;
        }
        if (!_contentDragging)
        {
            // Allow a stationary click, then retain the original grab point exactly.
            if (Math.Abs(point.Position.Y - _contentStartY) < 3) return;
            _contentDragging = true;
            // ButtonBase may already own capture. Release it before capturing the
            // list, both to cancel its click and to allow capture to succeed.
            for (var source = args.OriginalSource as DependencyObject; source is not null && !ReferenceEquals(source, Scroller);
                source = VisualTreeHelper.GetParent(source))
                if (source is UIElement element && element.PointerCaptures?.Any(pointer => pointer.PointerId == args.Pointer.PointerId) == true)
                    element.ReleasePointerCapture(args.Pointer);
            if (!Scroller.CapturePointer(args.Pointer)) { EndContentDrag(); return; }
            Focus(FocusState.Pointer);
        }
        SetScrollOffset(SchedulerScroll.DragOffset(_contentStartOffset, _contentStartY, point.Position.Y,
            Scroller.ExtentHeight, Scroller.ViewportHeight));
        args.Handled = true;
    }
    private void Scroller_PointerReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_contentPointer?.PointerId != args.Pointer.PointerId) return;
        if (_contentDragging)
        {
            SetScrollOffset(SchedulerScroll.DragOffset(_contentStartOffset, _contentStartY,
                args.GetCurrentPoint(Scroller).Position.Y, Scroller.ExtentHeight, Scroller.ViewportHeight));
            args.Handled = true;
        }
        EndContentDrag();
    }
    private void Scroller_PointerCanceled(object sender, PointerRoutedEventArgs args)
    {
        if (_contentPointer?.PointerId == args.Pointer.PointerId) EndContentDrag();
    }
    private void Scroller_PointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        // Capturing from a child button also raises its capture-lost event here.
        if (_contentPointer?.PointerId == args.Pointer.PointerId && (ReferenceEquals(args.OriginalSource, Scroller) || !_contentDragging)) EndContentDrag();
    }
    private void EndContentDrag()
    {
        var pointer = _contentPointer;
        _contentPointer = null;
        _contentDragging = false;
        if (pointer is not null) Scroller?.ReleasePointerCapture(pointer);
    }
    private void Scroller_PointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        if (_contentPointer is not null) { args.Handled = true; return; }
        var previous = _wheelAnimation is { } animation
            ? animation with { ElapsedMilliseconds = Stopwatch.GetElapsedTime(_wheelStartedAt).TotalMilliseconds }
            : (SchedulerWheelAnimation?)null;
        var motion = SchedulerScroll.PlanWheel(Scroller.VerticalOffset, args.GetCurrentPoint(this).Properties.MouseWheelDelta,
            Scroller.ExtentHeight, Scroller.ViewportHeight, _animatedScrollId >= 0, previous);
        AnimateScrollTo(motion);
        args.Handled = true;
    }
    private void Track_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_dragging || !args.GetCurrentPoint(TrackCanvas).Properties.IsLeftButtonPressed) return;
        for (var source = args.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
            if (ReferenceEquals(source, ScrollThumb)) return;
        var top = args.GetCurrentPoint(TrackCanvas).Position.Y - SchedulerScroll.ThumbHeight / 2;
        ScrollTo(SchedulerScroll.OffsetAtThumb(top, Scroller.ExtentHeight, Scroller.ViewportHeight));
        Focus(FocusState.Pointer);
        args.Handled = true;
    }
    private void Thumb_DragStarted(object sender, DragStartedEventArgs args)
    {
        _dragging = true;
        StopScrollAnimation();
        _dragTop = SchedulerScroll.ThumbTop(Scroller.VerticalOffset, Scroller.ExtentHeight, Scroller.ViewportHeight);
        SetThumbImage("pressed");
        Focus(FocusState.Pointer);
    }
    private void Thumb_DragDelta(object sender, DragDeltaEventArgs args)
    {
        _dragTop = Math.Clamp(_dragTop + args.VerticalChange, 2, Math.Max(2, Scroller.ViewportHeight - SchedulerScroll.ThumbHeight));
        ScrollTo(SchedulerScroll.OffsetAtThumb(_dragTop, Scroller.ExtentHeight, Scroller.ViewportHeight));
    }
    private void Thumb_DragCompleted(object sender, DragCompletedEventArgs args) { _dragging = false; SetThumbImage(_hover ? "hover" : "normal"); }
    private void Thumb_PointerEntered(object sender, PointerRoutedEventArgs args) { _hover = true; if (!_dragging) SetThumbImage("hover"); }
    private void Thumb_PointerExited(object sender, PointerRoutedEventArgs args) { _hover = false; if (!_dragging) SetThumbImage("normal"); }
    private void List_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        var offset = Scroller.VerticalOffset;
        switch (args.Key)
        {
            case VirtualKey.Up: offset -= SchedulerScroll.RowHeight; break;
            case VirtualKey.Down: offset += SchedulerScroll.RowHeight; break;
            case VirtualKey.PageUp: offset -= Scroller.ViewportHeight; break;
            case VirtualKey.PageDown: offset += Scroller.ViewportHeight; break;
            case VirtualKey.Home: offset = 0; break;
            case VirtualKey.End: offset = Scroller.ExtentHeight; break;
            default: return;
        }
        ScrollTo(offset);
        args.Handled = true;
    }
}
