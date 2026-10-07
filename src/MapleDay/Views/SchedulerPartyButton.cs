using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace MapleDay.Views;

// The WZ mouseOver canvas changes only the arrow glow, not the button background.
public sealed class SchedulerPartyButton : Button
{
    public static readonly DependencyProperty CompletedProperty = DependencyProperty.Register(
        nameof(Completed), typeof(bool), typeof(SchedulerPartyButton), new PropertyMetadata(false, Changed));
    public bool Completed { get => (bool)GetValue(CompletedProperty); set => SetValue(CompletedProperty, value); }
    private readonly Image _image = new() { Width = 66, Height = 22, Stretch = Stretch.None };
    private bool _hover;

    public SchedulerPartyButton()
    {
        Content = _image;
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => Draw());
        IsEnabledChanged += (_, _) => Draw();
        Unloaded += (_, _) => { _hover = false; Draw(); };
        Draw();
    }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs _) => ((SchedulerPartyButton)sender).Draw();
    private void Draw()
    {
        var name = Completed ? "btMoveCompleted" : "btMove";
        var state = !IsEnabled ? "disabled" : IsPressed ? "pressed" : _hover ? "mouseOver" : "normal";
        _image.Source = LocalImageCache.Get($"Scheduler/UI/main_entity_{name}_{state}_0.png");
    }

    protected override void OnPointerEntered(PointerRoutedEventArgs e) { base.OnPointerEntered(e); _hover = true; Draw(); }
    protected override void OnPointerExited(PointerRoutedEventArgs e) { base.OnPointerExited(e); _hover = false; Draw(); }
    protected override void OnPointerCanceled(PointerRoutedEventArgs e) { base.OnPointerCanceled(e); _hover = false; Draw(); }
    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (e.GetCurrentPoint(this).IsInContact) _hover = false;
        Draw();
    }
}
