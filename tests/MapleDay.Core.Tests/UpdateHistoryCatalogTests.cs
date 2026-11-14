using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class UpdateHistoryCatalogTests
{
    private static IReadOnlyList<UpdateHistoryEntry> ReadRepositoryHistory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, "docs", "update-history.json");
            if (File.Exists(file)) return UpdateHistoryCatalog.Parse(File.ReadAllText(file));
        }
        throw new FileNotFoundException("docs/update-history.json을 찾지 못했습니다.");
    }

    [Fact]
    public void EveryPublishedVersionHasDatedNonEmptyNotesInNewestFirstOrder()
    {
        var history = ReadRepositoryHistory();
        Assert.Equal(40, history.Count);
        Assert.Equal(history.Count, history.Select(item => item.Version).Distinct().Count());
        Assert.All(history, item => Assert.False(string.IsNullOrWhiteSpace(item.Notes)));
        Assert.DoesNotContain(history, item => item.Notes.Contains("관리자", StringComparison.Ordinal));
        Assert.All(history.Zip(history.Skip(1)), pair =>
            Assert.True(pair.First.Version > pair.Second.Version));
    }

    [Fact]
    public void HistoryUsesFiveEntriesPerPageAndKeepsTheFinalRemainder()
    {
        var history = ReadRepositoryHistory();
        Assert.Equal(8, UpdateHistoryCatalog.PageCount(history));
        Assert.Equal(5, UpdateHistoryCatalog.Page(history, 0).Count);
        Assert.Equal(5, UpdateHistoryCatalog.Page(history, 7).Count);
        Assert.Empty(UpdateHistoryCatalog.Page(history, -1));
        Assert.Empty(UpdateHistoryCatalog.Page(history, UpdateHistoryCatalog.PageCount(history)));
    }

    [Fact]
    public void SkippedVersionsIncludeEveryReleaseThroughLatest()
    {
        var history = ReadRepositoryHistory();
        var pending = UpdateHistoryCatalog.Between(history, new Version(1, 1, 33, 0), new Version(1, 2, 4, 0));

        Assert.Equal([new Version(1, 2, 4, 0), new Version(1, 2, 3, 0), new Version(1, 2, 2, 0), new Version(1, 2, 1, 0), new Version(1, 2, 0, 0),
            new Version(1, 1, 39, 0), new Version(1, 1, 35, 0), new Version(1, 1, 34, 0)],
            pending.Select(item => item.Version));
        var markdown = UpdateHistoryCatalog.ToMarkdown(pending);
        Assert.Contains("## 1.2.4.0", markdown, StringComparison.Ordinal);
        Assert.Contains("## 1.2.3.0", markdown, StringComparison.Ordinal);
        Assert.Contains("## 1.2.2.0", markdown, StringComparison.Ordinal);
        Assert.Contains("## 1.2.1.0", markdown, StringComparison.Ordinal);
        Assert.Contains("## 1.2.0.0", markdown, StringComparison.Ordinal);
        Assert.Contains("## 1.1.39.0", markdown, StringComparison.Ordinal);
        Assert.Contains("## 1.1.35.0", markdown, StringComparison.Ordinal);
        Assert.Contains("## 1.1.34.0", markdown, StringComparison.Ordinal);
    }
}
