using System.Globalization;
using System.Text.Json;

namespace MapleDay.Core;

public sealed record EquipmentStat(string Label, decimal Total, bool Percent, decimal? Base, decimal Bonus, decimal Scroll, decimal Starforce, decimal Exceptional)
{
    public string Value => Format(Total) + (Percent ? "%" : "");
    public string Breakdown => Base is null ? "" : string.Join(" · ", new[]
    {
        "기본 " + Format(Base.Value), Part("추옵", Bonus), Part("주문서", Scroll), Part("스타포스", Starforce), Part("익셉셔널", Exceptional),
        Part("기타", Total - Base.Value - Bonus - Scroll - Starforce - Exceptional)
    }.Where(text => !string.IsNullOrEmpty(text)));
    private static string Part(string label, decimal value) => value == 0 ? "" : label + " " + Format(value);
    public static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public IReadOnlyList<(decimal Value, string Kind)> Components => Base is null ? [] : new[]
    {
        (Base.Value, "base"), (Starforce, "starforce"), (Scroll, "scroll"), (Bonus, "bonus"), (Exceptional, "exceptional"),
        (Total - Base.Value - Bonus - Scroll - Starforce - Exceptional, "other")
    }.Where(part => part.Item2 == "base" || part.Item1 != 0).ToArray();
}

public static class EquipmentDetails
{
    private static readonly (string Key, string Label, bool Percent)[] Stats =
    [
        ("str", "STR", false), ("dex", "DEX", false), ("int", "INT", false), ("luk", "LUK", false),
        ("max_hp", "최대 HP", false), ("max_mp", "최대 MP", false), ("attack_power", "공격력", false),
        ("magic_power", "마력", false), ("armor", "방어력", false), ("speed", "이동속도", false), ("jump", "점프력", false),
        ("boss_damage", "보스 몬스터 데미지", true), ("ignore_monster_armor", "몬스터 방어율 무시", true),
        ("all_stat", "올스탯", true), ("damage", "데미지", true), ("max_hp_rate", "최대 HP", true), ("max_mp_rate", "최대 MP", true)
    ];

    public static string GradeKey(string? grade) => grade switch
    { "레어" => "rare", "에픽" => "epic", "유니크" => "unique", "레전드리" => "legendary", _ => "normal" };
    public static int Stars(EquipmentItem item) => int.TryParse(item.Starforce, out var stars) ? Math.Clamp(stars, 0, 50) : 0;
    public static string Title(EquipmentItem item) => (item.Name ?? "장비") + (Number(item.ScrollUpgrade) > 0 ? " (+" + item.ScrollUpgrade + ")" : "");
    public static IReadOnlyList<EquipmentStat> StatLines(EquipmentItem item) => Stats
        .Where(stat => Read(item.TotalOptions, stat.Key) is { } value && value != 0)
        .Select(stat => new EquipmentStat(stat.Label, Read(item.TotalOptions, stat.Key)!.Value, stat.Percent,
            Read(item.BaseOptions, stat.Key), Read(item.BonusOptions, stat.Key) ?? 0, Read(item.ScrollOptions, stat.Key) ?? 0,
            Read(item.StarforceOptions, stat.Key) ?? 0, Read(item.ExceptionalOptions, stat.Key) ?? 0)).ToArray();

    public static IReadOnlyList<string> UpgradeLines(EquipmentItem item)
    {
        var result = new List<string>();
        if (item.SpecialRingLevel > 0) result.Add($"특수 스킬 반지 Lv. {item.SpecialRingLevel}");
        if (Stars(item) > 0) result.Add($"스타포스 {Stars(item)}성" + (item.StarforceScroll == "사용" ? " · 스타포스 강화권 사용" : ""));
        if (item.ScrollUpgrade is not null || item.ScrollRemaining is not null)
        {
            var parts = new List<string>();
            if (item.ScrollUpgrade is not null) parts.Add("주문서 강화 " + item.ScrollUpgrade + "회");
            if (item.ScrollRemaining is not null) parts.Add("남은 횟수 " + item.ScrollRemaining + "회");
            if (Number(item.ScrollRestored) > 0) parts.Add("복구 " + item.ScrollRestored + "회");
            result.Add(string.Join(" · ", parts));
        }
        if (item.GoldenHammer is "적용" or "사용" or "1") result.Add("황금망치 적용");
        if (item.EquipmentLevelIncrease > 0) result.Add($"착용 레벨 증가 +{item.EquipmentLevelIncrease}");
        if (Read(item.BaseOptions, "base_equipment_level") is { } level && level > 0)
            result.Add("요구 레벨 " + EquipmentStat.Format(level + item.EquipmentLevelIncrease - (Read(item.TotalOptions, "equipment_level_decrease") ?? 0)));
        if (item.CuttableCount is { Length: > 0 } cuts && cuts != "255") result.Add("가위 사용 가능 횟수 " + cuts + "회");
        if (!string.IsNullOrWhiteSpace(item.Expires)) result.Add(item.Expires == "expired" ? "사용 기간 만료" : "사용 기간: " + item.Expires);
        return result;
    }
    public static decimal? Read(Dictionary<string, JsonElement>? values, string key) => values is not null && values.TryGetValue(key, out var value)
        && decimal.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    private static decimal Number(string? value) => EquipmentSymbol.Number(value);
}
