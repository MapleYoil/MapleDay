using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class SchedulerNumberGlyphsTests
{
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "SchedulerNumbers");
    private static IReadOnlyDictionary<string, int> Widths()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Assets, "manifest.json")));
        return manifest.RootElement.GetProperty("widths").Deserialize<Dictionary<string, int>>()!;
    }

    [Theory]
    [InlineData('0', "0030")]
    [InlineData('층', "ce35")]
    [InlineData('점', "c810")]
    [InlineData('인', "c778")]
    public void Character_key_uses_unicode_number_not_char_format(char character, string expected)
        => Assert.Equal(expected, SchedulerNumberGlyphs.Key(character));

    [Theory]
    [InlineData("0", " 층")]
    [InlineData("29,165", " 점")]
    [InlineData("10", " / 10")]
    [InlineData("100", " / 100")]
    [InlineData("STAGE 5", " / 5")]
    [InlineData("3", "인")]
    public void Actual_progress_glyphs_exist_and_include_units(string number, string unit)
    {
        var glyphs = SchedulerNumberGlyphs.Layout(number, unit, Widths(), 86);
        Assert.Equal((number + unit).Count(character => character != ' '), glyphs.Count);
        foreach (var glyph in glyphs) Assert.True(File.Exists(Path.Combine(Assets, $"{glyph.Color}_{glyph.Key}.png")));
        Assert.Equal(86, glyphs[^1].Left + glyphs[^1].Width);
        Assert.Contains(glyphs, glyph => glyph.Color == "unit");
    }

    [Fact]
    public void Weekly_header_uses_existing_white_glyphs_and_centers_limit()
    {
        var glyphs = SchedulerNumberGlyphs.Layout("12", "", Widths(), 20, center: true, white: true);
        Assert.Equal(3, glyphs[0].Left);
        Assert.All(glyphs, glyph => Assert.True(File.Exists(Path.Combine(Assets, $"white_{glyph.Key}.png"))));
    }
}
