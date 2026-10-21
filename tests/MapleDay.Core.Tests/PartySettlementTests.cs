using MapleDay.Core;
namespace MapleDay.Core.Tests;
public class PartySettlementTests
{
    private static PartyMember[] Members(int count) => Enumerable.Range(1, count).Select(i => new PartyMember(i.ToString(), "구성원 " + i)).ToArray();
    [Fact] public void TwoMemberAuctionExampleBalancesNetReceipts()
    {
        var result = PartySettlement.Calculate(500_000_000, Members(2), "1");
        Assert.Equal(485_000_000, result.SaleNet);
        Assert.Equal(246_192_893, result.Members[1].TransferAmount);
        Assert.Equal(238_807_106, result.Members[1].Received);
        Assert.Equal(238_807_107, result.Members[0].Received);
        Assert.Equal(result.Sale, result.Members.Sum(item => item.Received) + result.Fees);
    }
    [Theory] [InlineData(0)] [InlineData(3)] [InlineData(5)]
    public void AllSizesConserveMoneyAndWholeUnitRemainder(int fee)
    {
        for (int count = 1; count <= 6; count++)
        foreach (var mode in new[] { "auction", "trade", "none" })
        {
            var result = PartySettlement.Calculate(500_000_003, Members(count), "1", fee, mode);
            Assert.Equal(result.Sale, result.Fees + result.Members.Sum(item => item.Received));
            Assert.InRange(result.Difference, 0, 12);
            Assert.All(result.Members, row => Assert.True(row.Received >= 0));
        }
    }
    [Fact] public void WeightedPayoutAndPendingReceiptAreSeparate()
    {
        var members = Members(2); members[0] = members[0] with { Weight = 2 };
        var result = PartySettlement.Calculate(900, members, "1", 0, "none");
        Assert.Equal(600, result.Members[0].Received); Assert.Equal(300, result.Members[1].Received);
        var pending = new LootSettlement(members, "1", "2", 0, "none");
        Assert.Equal(0, pending.Received(900));
        Assert.Equal(100, (pending with { State = "partial", ActualReceived = 100 }).Received(900));
        Assert.Equal(300, (pending with { State = "complete" }).Received(900));
    }
    [Fact] public void FragmentsAreNotValuedTwice()
    {
        Assert.Equal("1억 메소 + 조각 150개", new IncomeDisplay("fragments").Hunting(100_000_000, 150, 850_000_000));
    }
    [Fact] public void CalendarStoresIndependentBossRosterAndLeader()
    {
        var boss = BossRecordChoices.Single.First();
        var party = new PartyPreset("party", "고정 파티", Members(3), "1", [new(boss.Name, boss.Difficulty, ["2", "3"])]);
        var schedule = Assert.Single(PartySettlement.Schedule(party, new(2026, 10, 18), null, true, null));
        Assert.True(ScheduleCalendar.Valid(schedule)); Assert.Equal(2, schedule.PartySize);
        Assert.Equal("2", schedule.Party!.LeaderId); Assert.DoesNotContain(schedule.Party.Members, member => member.Id == "1");
        party.Members[1] = party.Members[1] with { Name = "변경 후" }; party.Bosses[0].Members[0] = "1";
        Assert.Equal("구성원 2", schedule.Party.Members[0].Name); Assert.Equal("2", schedule.Party.Bosses[0].Members[0]);
    }
    [Fact] public void LootIncomeIncludesOnlyConfirmedReceiptsAndRejectsInvalidSnapshots()
    {
        var members = Members(2); var snapshot = new LootSettlement(members, "1", "2");
        var record = new BossLootRecord("loot", "ocid", new(2026, 10, 18), "스우", "", "정산", "settlement", 500_000_000, 2) { Settlement = snapshot };
        Assert.True(BossLoot.Valid(record)); Assert.Equal(0, record.Received);
        Assert.Equal(238_807_106, (record with { Settlement = snapshot with { State = "complete" } }).Received);
        Assert.False(BossLoot.Valid(record with { Settlement = snapshot with { Members = null! } }));
        Assert.False(BossLoot.Valid(record with { Settlement = snapshot with { OwnId = "missing", State = "complete" } }));
        Assert.False(BossLoot.Valid(record with { PartySize = 3 }));
    }
    [Fact] public void SharedPartyLootHidesOcidsAndProjectsTheSameSaleForEachMember()
    {
        var members = new[] { new PartyMember("leader", "파티장", "leader-ocid"), new PartyMember("member", "파티원", "member-ocid") };
        var party = new PartyPreset("shared", "공유 파티", members, "leader", [new("스우", "hard", ["leader", "member"])]);
        var settlement = new LootSettlement(members, "leader", "leader", 3, "auction", "complete");
        var local = new BossLootRecord("loot", "leader-ocid", new(2026, 10, 20), "스우", "", "물욕템 정산", "settlement",
            500_000_000, 2, Difficulty: "hard", SharedPartyId: party.Id, SharedUpdatedAt: 100) { Settlement = settlement };
        var sharedParty = PartyShareRecords.NormalizeParty(party);
        var shared = PartyShareRecords.NormalizeLoot(local, sharedParty);
        Assert.All(sharedParty.Members, member => Assert.Empty(member.Ocid));
        Assert.All(shared.Settlement!.Members, member => Assert.Empty(member.Ocid));
        Assert.StartsWith("shared-party:", shared.Ocid); Assert.Empty(shared.ClearId);
        var projected = PartyShareRecords.ProjectLoot(shared, new(party.Id, "member", "member-ocid"));
        Assert.Equal("member-ocid", projected.Ocid); Assert.Equal("member", projected.Settlement!.OwnId);
        Assert.Equal(238_807_106, projected.Received); Assert.Equal(local.Id, projected.Id);
    }
    [Fact] public void SharedPartyProjectionKeepsOnlyThisPcCharacterConnection()
    {
        var shared = new PartyPreset("party", "공유", [new("one", "한 명"), new("two", "두 명")], "one",
            [new("스우", "hard", ["one", "two"])]);
        var prior = shared with { Members = [new("one", "한 명", "old-one"), new("two", "두 명", "old-two")] };
        var projected = PartyShareRecords.ProjectParty(shared, new("party", "two", "new-two"), prior);
        Assert.Equal("old-one", projected.Members[0].Ocid); Assert.Equal("new-two", projected.Members[1].Ocid);
        Assert.Throws<ArgumentException>(() => PartyShareRecords.ProjectParty(shared, new("party", "missing", "x"), prior));
    }
}
