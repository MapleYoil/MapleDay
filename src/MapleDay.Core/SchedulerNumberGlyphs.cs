using System.Globalization;

namespace MapleDay.Core;

public sealed record SchedulerGlyph(string Key, string Color, double Left, int Width);

public static class SchedulerNumberGlyphs
{
    public static string Key(char character) => ((int)character).ToString("x4", CultureInfo.InvariantCulture);

    public static IReadOnlyList<SchedulerGlyph> Layout(string number, string unit, IReadOnlyDictionary<string, int> widths,
        double containerWidth, bool center = false, bool white = false)
    {
        var characters = number.Select(character => (Character: character, Color: white ? "white" : "number"))
            .Concat(unit.Select(character => (Character: character, Color: white ? "white" : "unit"))).ToArray();
        var total = characters.Sum(item => widths[Key(item.Character)]);
        var left = center ? Math.Floor((containerWidth - total) / 2) : containerWidth - total;
        var result = new List<SchedulerGlyph>();
        foreach (var (character, color) in characters)
        {
            var key = Key(character);
            var width = widths[key];
            if (character != ' ') result.Add(new(key, color, left, width));
            left += width;
        }
        return result;
    }
}
