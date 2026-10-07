using Microsoft.UI.Xaml.Media.Imaging;
using MapleDay.Services;

namespace MapleDay.Models;

internal static class WorldIconAssets
{
    private static readonly Dictionary<string, int> IconIds = new(StringComparer.Ordinal)
    {
        ["전체월드"] = 1,
        ["핼리오스"] = 2,
        ["에오스"] = 3,
        ["오로라"] = 4,
        ["레드"] = 5,
        ["이노시스"] = 6,
        ["유니온"] = 7,
        ["스카니아"] = 8,
        ["루나"] = 9,
        ["제니스"] = 10,
        ["크로아"] = 11,
        ["베라"] = 12,
        ["엘리시움"] = 13,
        ["아케인"] = 14,
        ["노바"] = 15,
        ["챌린저스"] = 20,
        ["챌린저스2"] = 21,
        ["챌린저스3"] = 22,
        ["챌린저스4"] = 23
    };

    public static WriteableBitmap? Create(string world) => IconIds.TryGetValue(world, out var id)
        ? LocalImageCache.Get($"Worlds/icon_{id}.png")
        : null;
}
