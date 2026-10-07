using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MapleDay.Models;

namespace MapleDay.Views;

public sealed partial class SchedulerBoard : UserControl
{
    public event EventHandler? NextCharacterRequested;
    public event EventHandler? ChooseCharacterRequested;
    public event EventHandler<PartySizeRequest>? PartySizeRequested;
    public SchedulerBoard() => InitializeComponent();
    private void NextCharacter_Click(object sender, RoutedEventArgs e) => NextCharacterRequested?.Invoke(this, EventArgs.Empty);
    private void ChooseCharacter_Click(object sender, RoutedEventArgs e) => ChooseCharacterRequested?.Invoke(this, EventArgs.Empty);
    private void PartySize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SchedulerTile tile } button) PartySizeRequested?.Invoke(this, new(tile, button));
    }
}

public sealed class PartySizeRequest(SchedulerTile tile, FrameworkElement anchor) : EventArgs
{
    public SchedulerTile Tile { get; } = tile;
    public FrameworkElement Anchor { get; } = anchor;
}
