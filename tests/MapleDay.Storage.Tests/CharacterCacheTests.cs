using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class CharacterCacheTests
{
    [Fact]
    public async Task RestartRestoresRosterExperienceAndImageOnlyForTheSameKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-Cache-Test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "characters.dat");
        try
        {
            var image = new CroppedCharacterImage(new byte[108 * 120 * 4], 108, 120);
            image.Pixels[0] = 42;
            await new CharacterCacheStore(path).SaveAsync("test-key", 2, [new(new CharacterSummary { Ocid = "one", Name = "캐릭터", World = "루나", Level = 285 },
                new CharacterBasic { Name = "캐릭터", Level = 285, ExpRate = "42.769" }, image)], CancellationToken.None);
            var restarted = new CharacterCacheStore(path);
            var cache = await restarted.LoadAsync("test-key");
            Assert.NotNull(cache);
            Assert.Equal(2, cache.AccountCount);
            var character = Assert.Single(cache.Characters);
            Assert.Equal("루나", character.Summary.World);
            Assert.Equal("42.769", character.Basic!.ExpRate);
            Assert.Equal(42, character.Image!.Pixels[0]);
            Assert.Null(await restarted.LoadAsync("different-key"));
            restarted.Delete();
            Assert.Null(await restarted.LoadAsync("test-key"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task MalformedCachedImageCannotReachTheBitmapDecoder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-Cache-Test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CharacterCacheStore(Path.Combine(directory, "characters.dat"));
            await store.SaveAsync("test-key", 1, [new(new CharacterSummary { Ocid = "one" }, Image: new([1, 2], 108, 120))], CancellationToken.None);
            var character = Assert.Single((await store.LoadAsync("test-key"))!.Characters);
            Assert.Null(character.Image);
            Assert.True(character.ImageFailed);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
