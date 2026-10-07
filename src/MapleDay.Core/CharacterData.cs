using System.Text.Json.Serialization;

namespace MapleDay.Core;

public sealed class CharacterListResponse
{
    [JsonPropertyName("account_list")]
    public List<MapleAccount> Accounts { get; init; } = [];
}

public sealed class MapleAccount
{
    [JsonPropertyName("account_id")]
    public string AccountId { get; init; } = "";

    [JsonPropertyName("character_list")]
    public List<CharacterSummary> Characters { get; init; } = [];
}

public sealed class CharacterSummary
{
    [JsonPropertyName("ocid")]
    public string Ocid { get; init; } = "";
    [JsonPropertyName("character_name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("world_name")]
    public string World { get; init; } = "";
    [JsonPropertyName("character_class")]
    public string Class { get; init; } = "";
    [JsonPropertyName("character_level")]
    public int Level { get; init; }
}

public sealed class CharacterBasic
{
    [JsonPropertyName("date")]
    public string? Date { get; init; }
    [JsonPropertyName("character_name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("world_name")]
    public string World { get; init; } = "";
    [JsonPropertyName("character_class")]
    public string Class { get; init; } = "";
    [JsonPropertyName("character_level")]
    public int? Level { get; init; }
    [JsonPropertyName("character_exp_rate")]
    public string? ExpRate { get; init; }
    [JsonPropertyName("character_exp")]
    public long? Exp { get; init; }
    [JsonPropertyName("character_date_create")]
    public string? CreatedDate { get; init; }
    [JsonPropertyName("character_image")]
    public string? ImageUrl { get; init; }
}

internal sealed class ApiErrorResponse
{
    [JsonPropertyName("error")]
    public ApiError? Error { get; init; }
}

internal sealed class ApiError
{
    [JsonPropertyName("name")]
    public string? Code { get; init; }
}
