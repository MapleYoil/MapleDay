using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class UpdateHistoryCatalogTests
{
    [Fact]
    public void EveryPublishedVersionHasDatedNonEmptyNotesInNewestFirstOrder()
    {
        Assert.Equal(34, UpdateHistoryCatalog.Entries.Count);
        Assert.Equal(UpdateHistoryCatalog.Entries.Count, UpdateHistoryCatalog.Entries.Select(item => item.Version).Distinct().Count());
        Assert.All(UpdateHistoryCatalog.Entries, item => Assert.False(string.IsNullOrWhiteSpace(item.Notes)));
        Assert.DoesNotContain(UpdateHistoryCatalog.Entries, item => item.Notes.Contains("관리자", StringComparison.Ordinal));
        Assert.All(UpdateHistoryCatalog.Entries.Zip(UpdateHistoryCatalog.Entries.Skip(1)), pair =>
            Assert.True(pair.First.Version > pair.Second.Version));
    }

    [Fact]
    public void HistoryUsesFiveEntriesPerPageAndKeepsTheFinalRemainder()
    {
        Assert.Equal(7, UpdateHistoryCatalog.PageCount);
        Assert.Equal(5, UpdateHistoryCatalog.Page(0).Count);
        Assert.Equal(4, UpdateHistoryCatalog.Page(6).Count);
        Assert.Empty(UpdateHistoryCatalog.Page(-1));
        Assert.Empty(UpdateHistoryCatalog.Page(UpdateHistoryCatalog.PageCount));
    }
}
