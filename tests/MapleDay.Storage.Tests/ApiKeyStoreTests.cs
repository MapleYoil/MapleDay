using System.Text;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class ApiKeyStoreTests
{
    [Fact]
    public async Task KeyIsEncryptedOnDiskAndCanBeLoadedReplacedAndRemoved()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MapleDay-Storage-Test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "api-key.dat");
        var store = new ApiKeyStore(path);
        const string key = "live_fake_storage_test_only";
        try
        {
            Assert.Null(await store.LoadAsync());
            await store.SaveAsync(key, CancellationToken.None);
            Assert.DoesNotContain(key, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)));
            Assert.Equal(key, await new ApiKeyStore(path).LoadAsync());
            await store.SaveAsync("live_replacement_test_only", CancellationToken.None);
            Assert.Equal("live_replacement_test_only", await store.LoadAsync());
            store.Delete();
            Assert.False(File.Exists(path));
            Assert.Null(await store.LoadAsync());
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }
}
