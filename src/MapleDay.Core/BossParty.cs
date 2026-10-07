namespace MapleDay.Core;

public static class BossParty
{
    public static int Maximum(string name) => SchedulerIconAssets.BossFile(name) is
        "Bosses/icon_35.png" or "Bosses/icon_37.png" or "Bosses/icon_33.png" or "Bosses/icon_41.png" or "Bosses/icon_38.png" or "Bosses/icon_34.png" ? 3 : 6;
    public static int Clamp(string name, int size) => Math.Clamp(size, 1, Maximum(name));
    public static string RecordId(string name, BossCycle cycle, DateOnly date) =>
        $"{cycle}:{SchedulerBossHistory.Start(cycle, date):yyyy-MM-dd}:{SchedulerBossHistory.BossKey(name)}";
}
