using MapleDay.Core;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class CharacterApiKeyMapTests
{
    [Fact]
    public void SchedulerCharactersKeepTheApiKeyForTheirOwnAccount()
    {
        var map = new CharacterApiKeyMap();
        map.Remember("first-key", [new CharacterSummary { Ocid = "first-character" }]);
        map.Remember("second-key", [new CharacterSummary { Ocid = "second-character" }]);

        Assert.Equal("first-key", map.Resolve("first-character"));
        Assert.Equal("second-key", map.Resolve("second-character"));
        Assert.Null(map.Resolve("not-loaded"));
    }

    [Fact]
    public void ARefreshedRosterReplacesStaleOwnership()
    {
        var map = new CharacterApiKeyMap();
        map.Remember("old-key", [new CharacterSummary { Ocid = "character" }]);
        map.Remember("new-key", [new CharacterSummary { Ocid = "character" }]);

        Assert.Equal("new-key", map.Resolve("character"));
    }
}
