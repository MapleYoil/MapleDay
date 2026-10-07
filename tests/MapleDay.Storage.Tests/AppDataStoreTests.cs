using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class AppDataStoreTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData(" 삭제", false)]
    [InlineData("삭제 ", false)]
    [InlineData("삭제\n", false)]
    [InlineData("삭제합니다", false)]
    [InlineData("삭제", true)]
    public void RequiresExactConfirmation(string text, bool accepted) => Assert.Equal(accepted, AppDataStore.IsDeleteConfirmation(text));

    [Fact]
    public async Task CountsNestedFilesAndDeletesOnlyTheExplicitDataDirectory()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "MapleDay-Data-Test-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(workspace, "data");
        Directory.CreateDirectory(Path.Combine(data, "scheduler-history", "character"));
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(data, "characters.dat"), new byte[1024]);
            await File.WriteAllBytesAsync(Path.Combine(data, "scheduler-history", "character", "2026-10-01.json"), new byte[2048]);
            var outside = Path.Combine(workspace, "keep.txt");
            await File.WriteAllTextAsync(outside, "preserve");
            var store = new AppDataStore(data);
            Assert.Equal(3072, await store.SizeAsync());
            await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync("삭제 "));
            Assert.True(File.Exists(Path.Combine(data, "characters.dat")));
            await store.DeleteAsync("삭제");
            Assert.False(Directory.Exists(data));
            Assert.Equal("preserve", await File.ReadAllTextAsync(outside));
            Assert.Equal(0, await store.SizeAsync());
        }
        finally { Directory.Delete(workspace, recursive: true); }
    }
}
