using MapleDay.Core;
using Microsoft.UI.Xaml;

namespace MapleDay.Models;

public sealed class ReminderCard(ReminderNotice notice)
{
    public string Id => notice.Id;
    public string Title => notice.Title;
    public string CreatedText => notice.CreatedText;
    public string Summary => notice.Summary;
    public string DeleteAccessibleName => $"{Title}, {CreatedText} 알림 삭제";
    public bool Read => notice.Read;
    public double Opacity => Read ? 0.7 : 1;
    public IReadOnlyList<ReminderContentCard> Contents { get; } = notice.Characters
        .SelectMany(character => character.Pending.Distinct(StringComparer.Ordinal).Select(content => (content, character)))
        .GroupBy(item => item.content, StringComparer.Ordinal)
        .Select(group => new ReminderContentCard(group.Key, notice.Kind,
            group.Select(item => item.character).DistinctBy(character => character.Ocid).ToArray())).ToArray();
}

public sealed class ReminderContentCard(string name, ReminderKind kind, IReadOnlyList<ReminderCharacter> characters)
{
    public string Name => name;
    public string Count => $"{characters.Count}캐릭터 미완료";
    public IReadOnlyList<ReminderCharacter> Characters => characters;
    private string BossName => name.LastIndexOf(" (", StringComparison.Ordinal) is var suffix && suffix >= 0 ? name[..suffix] : name;
    public string? IconFile => kind == ReminderKind.WeeklyBoss && SchedulerIconAssets.BossFile(BossName) is { } file ? "Scheduler/" + file : null;
    public Visibility IconVisibility => IconFile is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FallbackVisibility => IconFile is null ? Visibility.Visible : Visibility.Collapsed;
    public string Glyph => kind == ReminderKind.WeeklyBoss ? "\uE7C1" : "\uE73A";
}
