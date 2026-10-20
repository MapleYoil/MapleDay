using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapleDay.Core;

public sealed class CharacterItemEquipment
{
    [JsonPropertyName("date")] public string? Date { get; set; }
    [JsonPropertyName("preset_no")] public int? PresetNo { get; set; }
    [JsonPropertyName("item_equipment")] public List<EquipmentItem>? Items { get; set; }
    [JsonPropertyName("item_equipment_preset_1")] public List<EquipmentItem>? Preset1 { get; set; }
    [JsonPropertyName("item_equipment_preset_2")] public List<EquipmentItem>? Preset2 { get; set; }
    [JsonPropertyName("item_equipment_preset_3")] public List<EquipmentItem>? Preset3 { get; set; }
    [JsonPropertyName("title")] public EquipmentTitle? Title { get; set; }
    [JsonPropertyName("dragon_equipment")] public List<EquipmentItem>? Dragon { get; set; }
    [JsonPropertyName("mechanic_equipment")] public List<EquipmentItem>? Mechanic { get; set; }
    // Null means the API did not supply this preset; an empty list is an empty preset.
    public IReadOnlyList<EquipmentItem>? Select(int preset) => preset switch
    { 0 => Items, 1 => Preset1, 2 => Preset2, 3 => Preset3, _ => null };
}

public sealed class EquipmentTitle
{
    [JsonPropertyName("title_name")] public string? Name { get; set; }
    [JsonPropertyName("title_icon")] public string? Icon { get; set; }
    [JsonPropertyName("title_description")] public string? Description { get; set; }
}

public sealed class EquipmentItem
{
    [JsonPropertyName("item_equipment_part")] public string? Part { get; set; }
    [JsonPropertyName("item_equipment_slot")] public string? CurrentSlot { get; set; }
    [JsonPropertyName("equipment_slot")] public string? PresetSlot { get; set; }
    [JsonIgnore] public string Slot => CurrentSlot ?? PresetSlot ?? Part ?? "";
    [JsonPropertyName("item_name")] public string? Name { get; set; }
    [JsonPropertyName("item_icon")] public string? Icon { get; set; }
    [JsonPropertyName("item_description")] public string? Description { get; set; }
    [JsonPropertyName("starforce")] public string? Starforce { get; set; }
    [JsonPropertyName("starforce_scroll_flag")] public string? StarforceScroll { get; set; }
    [JsonPropertyName("potential_option_grade")] public string? Grade { get; set; }
    [JsonPropertyName("potential_option_1")] public string? Potential1 { get; set; }
    [JsonPropertyName("potential_option_2")] public string? Potential2 { get; set; }
    [JsonPropertyName("potential_option_3")] public string? Potential3 { get; set; }
    [JsonPropertyName("additional_potential_option_grade")] public string? AdditionalGrade { get; set; }
    [JsonPropertyName("additional_potential_option_1")] public string? Additional1 { get; set; }
    [JsonPropertyName("additional_potential_option_2")] public string? Additional2 { get; set; }
    [JsonPropertyName("additional_potential_option_3")] public string? Additional3 { get; set; }
    [JsonPropertyName("item_total_option")] public Dictionary<string, JsonElement>? TotalOptions { get; set; }
    [JsonPropertyName("item_base_option")] public Dictionary<string, JsonElement>? BaseOptions { get; set; }
    [JsonPropertyName("item_add_option")] public Dictionary<string, JsonElement>? BonusOptions { get; set; }
    [JsonPropertyName("item_etc_option")] public Dictionary<string, JsonElement>? ScrollOptions { get; set; }
    [JsonPropertyName("item_starforce_option")] public Dictionary<string, JsonElement>? StarforceOptions { get; set; }
    [JsonPropertyName("item_exceptional_option")] public Dictionary<string, JsonElement>? ExceptionalOptions { get; set; }
    [JsonPropertyName("scroll_upgrade")] public string? ScrollUpgrade { get; set; }
    [JsonPropertyName("scroll_upgradable_count")] public string? ScrollRemaining { get; set; }
    [JsonPropertyName("scroll_resilience_count")] public string? ScrollRestored { get; set; }
    [JsonPropertyName("golden_hammer_flag")] public string? GoldenHammer { get; set; }
    [JsonPropertyName("equipment_level_increase")] public long EquipmentLevelIncrease { get; set; }
    [JsonPropertyName("special_ring_level")] public long SpecialRingLevel { get; set; }
    [JsonPropertyName("soul_name")] public string? SoulName { get; set; }
    [JsonPropertyName("soul_option")] public string? SoulOption { get; set; }
    [JsonPropertyName("date_expire")] public string? Expires { get; set; }
    [JsonPropertyName("cuttable_count")] public string? CuttableCount { get; set; }
}

public sealed class CharacterCashEquipment
{
    [JsonPropertyName("date")] public string? Date { get; set; }
    [JsonPropertyName("character_look_mode")] public string? LookMode { get; set; }
    [JsonPropertyName("preset_no")] public int? PresetNo { get; set; }
    [JsonPropertyName("cash_item_equipment_base")] public List<CashEquipmentItem>? Base { get; set; }
    [JsonPropertyName("cash_item_equipment_preset_1")] public List<CashEquipmentItem>? Preset1 { get; set; }
    [JsonPropertyName("cash_item_equipment_preset_2")] public List<CashEquipmentItem>? Preset2 { get; set; }
    [JsonPropertyName("cash_item_equipment_preset_3")] public List<CashEquipmentItem>? Preset3 { get; set; }
    [JsonPropertyName("additional_cash_item_equipment_base")] public List<CashEquipmentItem>? AdditionalBase { get; set; }
    [JsonPropertyName("additional_cash_item_equipment_preset_1")] public List<CashEquipmentItem>? Additional1 { get; set; }
    [JsonPropertyName("additional_cash_item_equipment_preset_2")] public List<CashEquipmentItem>? Additional2 { get; set; }
    [JsonPropertyName("additional_cash_item_equipment_preset_3")] public List<CashEquipmentItem>? Additional3 { get; set; }
    public IReadOnlyList<CashEquipmentItem>? Select(int preset, bool additional = false) => (preset, additional) switch
    {
        (0, false) => Base, (1, false) => Preset1, (2, false) => Preset2, (3, false) => Preset3,
        (0, true) => AdditionalBase, (1, true) => Additional1, (2, true) => Additional2, (3, true) => Additional3, _ => null
    };
}

public sealed class CashEquipmentItem
{
    [JsonPropertyName("cash_item_equipment_part")] public string? Part { get; set; }
    [JsonPropertyName("cash_item_equipment_slot")] public string? Slot { get; set; }
    [JsonPropertyName("cash_item_name")] public string? Name { get; set; }
    [JsonPropertyName("cash_item_icon")] public string? Icon { get; set; }
    [JsonPropertyName("cash_item_description")] public string? Description { get; set; }
    [JsonPropertyName("cash_item_label")] public string? Label { get; set; }
    [JsonPropertyName("date_expire")] public string? Expires { get; set; }
}

public enum SymbolKind { Arcane, Authentic, GrandAuthentic }

public sealed class CharacterSymbolEquipment
{
    [JsonPropertyName("date")] public string? Date { get; set; }
    [JsonPropertyName("symbol")] public List<EquipmentSymbol>? Symbols { get; set; }
    public decimal TotalForce(SymbolKind kind) => (Symbols ?? []).Where(symbol =>
        kind == SymbolKind.Arcane ? symbol.Kind == SymbolKind.Arcane : symbol.Kind != SymbolKind.Arcane)
        .Sum(symbol => EquipmentSymbol.Number(symbol.Force));

    public IReadOnlyList<EquipmentSymbol?> Slots(SymbolKind kind)
    {
        var regions = kind switch
        {
            SymbolKind.Arcane => new[] { "소멸의여로", "츄츄아일랜드", "레헬른", "아르카나", "모라스", "에스페라" },
            SymbolKind.Authentic => ["세르니움", "아르크스", "오디움", "도원경", "아르테리아", "카르시온"],
            _ => []
        };
        var result = new EquipmentSymbol?[kind == SymbolKind.GrandAuthentic ? 3 : 6];
        var remaining = new List<EquipmentSymbol>();
        foreach (var symbol in (Symbols ?? []).Where(symbol => symbol.Kind == kind))
        {
            var normalized = (symbol.Name ?? "").Replace(" ", "", StringComparison.Ordinal);
            var index = Array.FindIndex(regions, region => normalized.Contains(region, StringComparison.Ordinal));
            if (index >= 0 && result[index] is null) result[index] = symbol;
            else remaining.Add(symbol);
        }
        foreach (var symbol in remaining)
        {
            var empty = Array.FindIndex(result, item => item is null);
            if (empty < 0) break;
            result[empty] = symbol;
        }
        return result;
    }
}

public sealed class EquipmentSymbol
{
    [JsonPropertyName("symbol_name")] public string? Name { get; set; }
    [JsonPropertyName("symbol_icon")] public string? Icon { get; set; }
    [JsonPropertyName("symbol_description")] public string? Description { get; set; }
    [JsonPropertyName("symbol_force")] public string? Force { get; set; }
    [JsonPropertyName("symbol_level")] public int Level { get; set; }
    [JsonPropertyName("symbol_str")] public string? Str { get; set; }
    [JsonPropertyName("symbol_dex")] public string? Dex { get; set; }
    [JsonPropertyName("symbol_int")] public string? Int { get; set; }
    [JsonPropertyName("symbol_luk")] public string? Luk { get; set; }
    [JsonPropertyName("symbol_hp")] public string? Hp { get; set; }
    [JsonPropertyName("symbol_exp_rate")] public string? Exp { get; set; }
    [JsonPropertyName("symbol_meso_rate")] public string? Meso { get; set; }
    [JsonPropertyName("symbol_drop_rate")] public string? Drop { get; set; }
    [JsonPropertyName("symbol_growth_count")] public long Growth { get; set; }
    [JsonPropertyName("symbol_require_growth_count")] public long RequiredGrowth { get; set; }
    [JsonIgnore] public SymbolKind Kind => Name?.Contains("그랜드", StringComparison.Ordinal) == true
        || Number(Exp) > 0 || Number(Meso) > 0 || Number(Drop) > 0
        ? SymbolKind.GrandAuthentic : Name?.Contains("어센틱", StringComparison.Ordinal) == true ? SymbolKind.Authentic : SymbolKind.Arcane;
    public static decimal Number(string? value) => decimal.TryParse(value?.TrimEnd('%'), NumberStyles.Number,
        CultureInfo.InvariantCulture, out var result) ? result : 0;
}

public static class EquipmentSlots
{
    public static readonly IReadOnlyDictionary<string, string> Gear = new Dictionary<string, string>
    {
        ["1"] = "모자", ["2"] = "얼굴장식", ["3"] = "눈장식", ["4"] = "귀고리", ["5"] = "상의", ["6"] = "하의",
        ["7"] = "신발", ["8"] = "장갑", ["9"] = "망토", ["10"] = "보조무기", ["11"] = "무기", ["12"] = "반지1",
        ["13"] = "반지2", ["15"] = "반지3", ["16"] = "반지4", ["17"] = "펜던트1", ["31"] = "펜던트2",
        ["21"] = "훈장", ["22"] = "벨트", ["23"] = "어깨장식", ["26"] = "포켓 아이템", ["27"] = "안드로이드",
        ["28"] = "기계 심장", ["29"] = "뱃지", ["30"] = "엠블렘", ["32"] = "보조무기", ["title"] = "칭호"
    };
    public static string Normalize(string? slot) => (slot ?? "").Replace(" ", "", StringComparison.Ordinal) switch
    { "한벌옷" => "상의", "방패" => "보조무기", "보조무기" => "보조무기", "배지" => "뱃지", "펜던트" => "펜던트1", var value => value };
    public static bool Matches(string? actual, string expected) => Normalize(actual) == Normalize(expected);
    public static bool IsPublicIcon(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Host.Equals("open.api.nexon.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("file.nexon.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("lwi.nexon.com", StringComparison.OrdinalIgnoreCase));
}
