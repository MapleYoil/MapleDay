using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class CharacterRepresentativeTests
{
    private static readonly CharacterSummary[] Roster = [new() { Ocid = "low", Level = 220 }, new() { Ocid = "high", Level = 290 }];
    [Fact] public void DefaultsToHighestLevel() => Assert.Equal("high", CharacterRepresentative.Choose(Roster, null));
    [Fact] public void PreservesAnExplicitChoice() => Assert.Equal("low", CharacterRepresentative.Choose(Roster, "low"));
    [Fact] public void MissingChoiceFallsBackToHighestLevel() => Assert.Equal("high", CharacterRepresentative.Choose(Roster, "other-key-character"));
    [Fact] public void EmptyRosterHasNoRepresentative() => Assert.Null(CharacterRepresentative.Choose([], "old"));
}
