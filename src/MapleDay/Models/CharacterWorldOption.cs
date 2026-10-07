using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MapleDay.Models;

public sealed class CharacterWorldOption(string? world)
{
    public string? World { get; } = world;
    public string Name => World is null ? "전체 서버" : string.IsNullOrWhiteSpace(World) ? "서버 정보 없음" : World;
    public WriteableBitmap? Icon { get; } = WorldIconAssets.Create(world ?? "전체월드");
    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;
}
