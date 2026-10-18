using System.Globalization;

namespace MapleDay.Core;

public sealed record PartyMember(string Id, string Name, string Ocid = "", decimal Weight = 1);
public sealed record PartyBoss(string Name, string Difficulty, string[] Members);
public sealed record PartyPreset(string Id, string Name, PartyMember[] Members, string LeaderId,
    PartyBoss[] Bosses, int AuctionFee = 3, string Transfer = "auction", string Note = "");
public sealed record PartyPayout(string MemberId, string Name, long TransferAmount, long Received, bool Leader);
public sealed record SettlementResult(long Sale, long SaleNet, long Fees, IReadOnlyList<PartyPayout> Members)
{
    public long Difference => Members.Max(item => item.Received) - Members.Min(item => item.Received);
}
public sealed record LootSettlement(PartyMember[] Members, string LeaderId, string OwnId,
    int AuctionFee = 3, string Transfer = "auction", string State = "pending", long ActualReceived = 0)
{
    public SettlementResult Calculate(long sale) => PartySettlement.Calculate(sale, Members, LeaderId, AuctionFee, Transfer);
    public long Received(long sale) => State == "complete" ? Calculate(sale).Members.Single(item => item.MemberId == OwnId).Received
        : State == "partial" ? ActualReceived : 0;
}

public static class PartySettlement
{
    public static bool Valid(PartyPreset? party) => party is not null && party.Id is { Length: > 0 and <= 100 } && party.Name?.Trim().Length is > 0 and <= 80
        && party.Note is { Length: <= 2000 } && ValidMembers(party.Members, party.LeaderId)
        && party.AuctionFee is 0 or 3 or 5 && party.Transfer is "auction" or "trade" or "none"
        && party.Bosses is { Length: <= 100 } && party.Bosses.All(boss => boss is not null && BossRecordChoices.Single.Any(item => item.Name == boss.Name && item.Difficulty == boss.Difficulty)
            && boss.Members is { Length: > 0 } && boss.Members.Length <= BossParty.Maximum(boss.Name)
            && boss.Members.Distinct().Count() == boss.Members.Length && boss.Members.All(id => party.Members.Any(member => member.Id == id)));
    public static bool ValidMembers(IReadOnlyList<PartyMember>? members, string leader) => members is { Count: >= 1 and <= 6 }
        && members.All(member => member is not null && member.Id is { Length: > 0 and <= 100 } && member.Name?.Trim().Length is > 0 and <= 50 && member.Ocid is { Length: <= 200 }
            && member.Weight is >= 0 and <= 1_000_000)
        && members.Select(member => member.Id).Distinct(StringComparer.Ordinal).Count() == members.Count
        && members.Any(member => member.Id == leader) && members.Sum(member => member.Weight) > 0;
    public static long Net(long amount, int fee) => amount is >= 0 and <= BossLoot.MaximumAmount && fee is 0 or 3 or 5
        ? (long)decimal.Floor(amount * (100m - fee) / 100) : throw new ArgumentException("금액 또는 수수료를 확인해주세요.");
    public static long Gross(long net, int fee) => net is >= 0 and <= BossLoot.MaximumAmount && fee is 0 or 3 or 5
        ? checked((long)decimal.Ceiling(net * 100m / (100 - fee))) : throw new ArgumentException("금액 또는 수수료를 확인해주세요.");
    public static SettlementResult Calculate(long sale, IReadOnlyList<PartyMember> members, string leader, int auctionFee = 3, string transfer = "auction")
    {
        if (!ValidMembers(members, leader) || transfer is not ("auction" or "trade" or "none"))
            throw new ArgumentException("파티원·파티장·분배 비율을 확인해주세요.");
        var net = Net(sale, auctionFee);
        var fee = transfer == "auction" ? auctionFee : transfer == "trade" ? 5 : 0;
        var keep = (100m - fee) / 100;
        var denominator = members.Sum(member => member.Weight / (member.Id == leader ? 1 : keep));
        var unit = net / denominator;
        long spent = 0;
        var payouts = members.Select(member =>
        {
            if (member.Id == leader) return new PartyPayout(member.Id, member.Name, 0, 0, true);
            var gross = (long)decimal.Floor(unit * member.Weight / keep);
            spent = checked(spent + gross);
            return new PartyPayout(member.Id, member.Name, gross, Net(gross, fee), false);
        }).ToArray();
        for (var i = 0; i < payouts.Length; i++)
            if (payouts[i].Leader) payouts[i] = payouts[i] with { Received = net - spent };
        return new(sale, net, sale - payouts.Sum(item => item.Received), payouts);
    }
    public static string Weights(IEnumerable<PartyMember> members) => string.Join(":", members.Select(member => member.Weight.ToString(CultureInfo.InvariantCulture)));
    public static PartyPreset ForBoss(PartyPreset party, string name, string difficulty)
    {
        var boss = party.Bosses.FirstOrDefault(slot => slot.Name == name && slot.Difficulty == difficulty);
        if (boss is null) return party;
        var members = party.Members.Where(member => boss.Members.Contains(member.Id)).Select(member => member with { }).ToArray();
        return party with { Members = members, LeaderId = members.Any(member => member.Id == party.LeaderId) ? party.LeaderId : members[0].Id,
            Bosses = [boss with { Members = boss.Members.ToArray() }] };
    }
    public static IReadOnlyList<CalendarSchedule> Schedule(PartyPreset party, DateOnly date, TimeOnly? time, bool weekly, DateOnly? until)
    {
        if (!Valid(party) || party.Bosses.Length == 0) throw new ArgumentException("파티와 보스를 먼저 등록해주세요.");
        return party.Bosses.Select(boss =>
        {
            var members = party.Members.Where(member => boss.Members.Contains(member.Id)).ToArray();
            var own = members.FirstOrDefault(member => !string.IsNullOrEmpty(member.Ocid)) ?? members[0];
            return new CalendarSchedule(Guid.NewGuid().ToString("N"), ScheduleKind.Boss,
                party.Name + " · " + boss.Name, date, time, party.Note, own.Ocid, own.Name, boss.Name, boss.Difficulty, weekly, until, members.Length)
                { Party = ForBoss(party, boss.Name, boss.Difficulty) };
        }).ToArray();
    }
}
