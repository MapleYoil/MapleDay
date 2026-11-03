namespace MapleDay.Core;

public static class AnnouncementPolicy
{
    public static bool IsForInstallation(long created, DateTimeOffset installedAt)
    {
        try { return DateTimeOffset.FromUnixTimeSeconds(created) >= installedAt; }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
