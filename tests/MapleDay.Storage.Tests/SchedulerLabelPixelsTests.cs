using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class SchedulerLabelPixelsTests
{
    [Fact]
    public void Label_renders_in_memory_without_a_window_and_repeats_identically()
    {
        using var renderer = new SchedulerLabelPixels(Path.Combine(AppContext.BaseDirectory, "NanumGothicBold.ttf"));
        var first = renderer.Render("[일일 퀘스트] 세르니움 조사", 165, 18, 59, 84, 110);
        var second = renderer.Render("[일일 퀘스트] 세르니움 조사", 165, 18, 59, 84, 110);
        Assert.Equal(165 * 18 * 4, first.Length);
        Assert.Equal(first, second);
        Assert.All(Enumerable.Range(0, 165 * 18), pixel => Assert.Equal(255, first[pixel * 4 + 3]));
        Assert.Contains(Enumerable.Range(0, 165 * 18), pixel => first[pixel * 4] == 110 && first[pixel * 4 + 1] == 84 && first[pixel * 4 + 2] == 59);
    }
}
