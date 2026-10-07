using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class ApiKeyProfileTests
{
    [Fact]
    public async Task KeyChoicesAndRepresentativesSurviveRestartWithoutPlaintextKeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-Profiles-Test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "profiles.dat");
        try
        {
            var store = new ApiKeyProfileStore(path);
            await store.SaveAsync([new("fake-key-a", "char-a", "캐릭터 A", "루나"), new("fake-key-b", "char-b", "캐릭터 B", "베라")]);
            var restart = new ApiKeyProfileStore(path);
            var profiles = await restart.LoadAsync();
            Assert.Equal(2, profiles.Count);
            Assert.Equal("char-a", profiles.Single(profile => profile.Key == "fake-key-a").RepresentativeOcid);
            Assert.Equal("char-b", profiles.Single(profile => profile.Key == "fake-key-b").RepresentativeOcid);
            await restart.SaveAsync(profiles.Select(profile => profile.Key == "fake-key-b" ? new ApiKeyProfile(profile.Key, "changed", profile.Name, profile.World) : profile));
            Assert.Equal("char-a", (await restart.LoadAsync()).Single(profile => profile.Key == "fake-key-a").RepresentativeOcid);
            Assert.DoesNotContain("fake-key-a", await File.ReadAllTextAsync(path));
            Assert.DoesNotContain("fake-key-b", await File.ReadAllTextAsync(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SwitchingKeysPreservesBothCachesAndMigratesTheLegacyRoster()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MapleDay-Profiles-Test-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(directory, "characters.dat");
        try
        {
            await new CharacterCacheStore(legacy).SaveAsync("fake-key-a", 1, [new(new CharacterSummary { Ocid = "char-a" })], default);
            var store = new CharacterCacheStore(legacy, Path.Combine(directory, "cache"));
            Assert.Equal("char-a", (await store.LoadAsync("fake-key-a"))!.Characters.Single().Summary.Ocid);
            await store.SaveAsync("fake-key-a", 1, [new(new CharacterSummary { Ocid = "char-a" })], default);
            await store.SaveAsync("fake-key-b", 1, [new(new CharacterSummary { Ocid = "char-b" })], default);
            var restart = new CharacterCacheStore(legacy, Path.Combine(directory, "cache"));
            Assert.Equal("char-a", (await restart.LoadAsync("fake-key-a"))!.Characters.Single().Summary.Ocid);
            Assert.Equal("char-b", (await restart.LoadAsync("fake-key-b"))!.Characters.Single().Summary.Ocid);
            Assert.Null(await restart.LoadAsync("fake-key-c"));
            Assert.True(File.Exists(legacy));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
