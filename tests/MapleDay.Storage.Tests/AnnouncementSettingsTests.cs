using System.Text.Json;
using MapleDay.Services;

namespace MapleDay.Storage.Tests;

public sealed class AnnouncementSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mapleday-announcement-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void LegacySettingsUseTheLocalDataFolderCreationTimeAndPersistIt()
    {
        Directory.CreateDirectory(_root);
        var expected = DateTime.UtcNow.AddDays(-10);
        Directory.SetCreationTimeUtc(_root, expected);
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{}");

        var settings = AppSettings.Load(path);
        Assert.True(settings.InstallationDateNeedsSave);
        Assert.InRange(settings.InstalledAt!.Value, new DateTimeOffset(expected.AddSeconds(-1), TimeSpan.Zero), new DateTimeOffset(expected.AddSeconds(1), TimeSpan.Zero));

        settings.Save(path);
        Assert.False(settings.InstallationDateNeedsSave);
        var restored = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))!;
        Assert.Equal(settings.InstalledAt, restored.InstalledAt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
