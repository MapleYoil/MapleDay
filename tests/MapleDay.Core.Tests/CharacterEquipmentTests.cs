using System.Net;
using System.Text;
using System.Text.Json;
using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class CharacterEquipmentTests
{
    [Theory]
    [InlineData("어센틱심볼 : 아르크스")]
    [InlineData("어센틱심볼 : 호텔 아르크스")]
    [InlineData("어센틱심볼:호텔아르크스")]
    public void ArcsAlwaysOccupiesSecondAuthenticSlot(string name)
    {
        var arcs = new EquipmentSymbol { Name = name, Level = 7, Force = "70" };
        var response = new CharacterSymbolEquipment { Symbols = [new() { Name = "어센틱심볼 : 카르시온" }, arcs, new() { Name = "어센틱심볼 : 세르니움" }] };
        var slots = response.Slots(SymbolKind.Authentic);
        Assert.Equal(6, slots.Count); Assert.Same(arcs, slots[1]);
        Assert.Contains("세르니움", slots[0]!.Name); Assert.Contains("카르시온", slots[5]!.Name);
        Assert.Null(slots[2]); Assert.Equal(70, response.TotalForce(SymbolKind.Authentic));
    }

    [Fact]
    public void GrandSymbolWithOlderNameAndRatesStillUsesGrandPanel()
    {
        var symbol = new EquipmentSymbol { Name = "어센틱심볼 : 탈라하트", Exp = "22", Meso = "8", Drop = "8", Force = "40" };
        var response = new CharacterSymbolEquipment { Symbols = [symbol] };
        Assert.Equal(SymbolKind.GrandAuthentic, symbol.Kind);
        Assert.Same(symbol, response.Slots(SymbolKind.GrandAuthentic)[0]);
        Assert.All(response.Slots(SymbolKind.Authentic), Assert.Null);
        Assert.Equal(40, response.TotalForce(SymbolKind.Authentic));
    }

    [Fact]
    public void EveryStandardSymbolSurvivesShuffledApiOrder()
    {
        var names = new[] { "세르니움", "아르크스", "오디움", "도원경", "아르테리아", "카르시온" };
        var response = new CharacterSymbolEquipment { Symbols = names.Reverse().Select(name => new EquipmentSymbol { Name = "어센틱심볼 : " + name }).ToList() };
        Assert.Equal(names.Select(name => "어센틱심볼 : " + name), response.Slots(SymbolKind.Authentic).Select(item => item?.Name));
    }

    [Fact]
    public void UnknownSymbolNameIsNotSilentlyDiscarded()
    {
        var symbol = new EquipmentSymbol { Name = "어센틱심볼 : 새 지역" };
        Assert.Contains(symbol, new CharacterSymbolEquipment { Symbols = [symbol] }.Slots(SymbolKind.Authentic));
    }

    [Fact]
    public void TooltipKeepsUpgradeCountsAndStatComponentsSeparate()
    {
        var item = JsonSerializer.Deserialize<EquipmentItem>("""
            {"item_name":"장비","starforce":"18","scroll_upgrade":"5","scroll_upgradable_count":"2","scroll_resilience_count":"1",
             "golden_hammer_flag":"적용","special_ring_level":4,"equipment_level_increase":0,"cuttable_count":"5",
             "item_total_option":{"str":"47","boss_damage":"30","armor":"15","max_hp_rate":"5"},
             "item_base_option":{"str":"10","boss_damage":"30","base_equipment_level":200},
             "item_add_option":{"str":"20"},"item_etc_option":{"str":"7"},"item_starforce_option":{"str":"10"}}
            """)!;
        Assert.Equal("장비 (+5)", EquipmentDetails.Title(item));
        Assert.Equal(18, EquipmentDetails.Stars(item));
        var str = Assert.Single(EquipmentDetails.StatLines(item), stat => stat.Label == "STR");
        Assert.Equal(47, str.Total); Assert.Equal(10, str.Base); Assert.Equal(20, str.Bonus); Assert.Equal(7, str.Scroll); Assert.Equal(10, str.Starforce);
        Assert.Contains("스타포스 10", str.Breakdown); Assert.DoesNotContain("기타", str.Breakdown);
        Assert.Equal("30%", Assert.Single(EquipmentDetails.StatLines(item), stat => stat.Label == "보스 몬스터 데미지").Value);
        var upgrade = EquipmentDetails.UpgradeLines(item);
        Assert.Contains("스타포스 18성", upgrade); Assert.Contains("특수 스킬 반지 Lv. 4", upgrade);
        Assert.Contains("주문서 강화 5회 · 남은 횟수 2회 · 복구 1회", upgrade);
        Assert.Contains("황금망치 적용", upgrade); Assert.Contains("요구 레벨 200", upgrade);
    }

    [Fact]
    public void MissingBaseStatsDoNotInventBreakdownOrUpgradeStatus()
    {
        var item = JsonSerializer.Deserialize<EquipmentItem>("""{"item_total_option":{"attack_power":"123"},"item_name":"장비"}""")!;
        Assert.Empty(EquipmentDetails.UpgradeLines(item));
        var stat = Assert.Single(EquipmentDetails.StatLines(item));
        Assert.Null(stat.Base); Assert.Equal("", stat.Breakdown); Assert.Equal("123", stat.Value);
        Assert.Equal("normal", EquipmentDetails.GradeKey(null));
    }

    [Fact]
    public void EquipmentPresetsUseEquipmentSlotAndDoNotFallBackToCurrentlyEquippedItems()
    {
        var response = JsonSerializer.Deserialize<CharacterItemEquipment>("""
            {"preset_no":2,"item_equipment":[{"item_equipment_slot":"반지1","item_name":"현재 반지"}],
             "item_equipment_preset_1":[],"item_equipment_preset_2":[{"equipment_slot":"펜던트2","item_name":"별도 프리셋"}],
             "title":{"title_name":"칭호","title_icon":"https://open.api.nexon.com/title.png"}}
            """)!;
        Assert.Equal("반지1", Assert.Single(response.Select(0)!).Slot);
        Assert.Empty(response.Select(1)!);
        Assert.Equal("펜던트2", Assert.Single(response.Select(2)!).Slot);
        Assert.Null(response.Select(3)); Assert.Null(response.Select(4));
        Assert.Equal("칭호", response.Title!.Name);
    }

    [Fact]
    public void CashPresetsAndAdditionalLookAreIndependent()
    {
        var response = JsonSerializer.Deserialize<CharacterCashEquipment>("""
            {"cash_item_equipment_base":[{"cash_item_equipment_slot":"모자","cash_item_name":"현재"}],
             "cash_item_equipment_preset_1":[],
             "cash_item_equipment_preset_3":[{"cash_item_equipment_slot":"반지4","cash_item_name":"기본 외형"}],
             "additional_cash_item_equipment_preset_3":[{"cash_item_equipment_slot":"반지4","cash_item_name":"추가 외형"}]}
            """)!;
        Assert.Equal("현재", Assert.Single(response.Select(0)!).Name);
        Assert.Empty(response.Select(1)!); Assert.Null(response.Select(2));
        Assert.Equal("기본 외형", Assert.Single(response.Select(3)!).Name);
        Assert.Equal("추가 외형", Assert.Single(response.Select(3, true)!).Name);
        Assert.Null(response.Select(0, true));
    }

    [Fact]
    public void SymbolKindsAndGrandAuthenticRatesAreRetained()
    {
        var response = JsonSerializer.Deserialize<CharacterSymbolEquipment>("""
            {"symbol":[{"symbol_name":"아케인심볼 : 에스페라","symbol_level":20,"symbol_force":"220"},
            {"symbol_name":"어센틱심볼 : 카르시온","symbol_level":7,"symbol_force":"70","symbol_dex":"10000"},
            {"symbol_name":"그랜드 어센틱심볼 : 탈라하트","symbol_level":4,"symbol_force":"50",
             "symbol_exp_rate":"22","symbol_meso_rate":"8","symbol_drop_rate":"8","symbol_growth_count":25,"symbol_require_growth_count":100}]}
            """)!;
        Assert.Equal(new[] { SymbolKind.Arcane, SymbolKind.Authentic, SymbolKind.GrandAuthentic }, response.Symbols!.Select(symbol => symbol.Kind));
        var grand = response.Symbols![2]; Assert.Equal(50, EquipmentSymbol.Number(grand.Force));
        Assert.Equal(220, response.TotalForce(SymbolKind.Arcane));
        Assert.Equal(120, response.TotalForce(SymbolKind.Authentic));
        Assert.Equal(120, response.TotalForce(SymbolKind.GrandAuthentic));
        Assert.Equal(22, EquipmentSymbol.Number(grand.Exp)); Assert.Equal(8, EquipmentSymbol.Number(grand.Meso));
        Assert.Equal(8, EquipmentSymbol.Number(grand.Drop)); Assert.Equal(25, grand.Growth);
        Assert.Equal(12.5m, EquipmentSymbol.Number("12.5%")); Assert.Equal(0, EquipmentSymbol.Number(null));
    }

    [Theory]
    [InlineData("반지1", "반지1", true)]
    [InlineData("반지2", "반지1", false)]
    [InlineData("펜던트2", "펜던트1", false)]
    [InlineData("기계심장", "기계 심장", true)]
    [InlineData("한벌옷", "상의", true)]
    [InlineData("포켓 아이템", "포켓아이템", true)]
    public void SlotsKeepRingAndPendantPositionsDistinct(string actual, string expected, bool matches)
        => Assert.Equal(matches, EquipmentSlots.Matches(actual, expected));

    [Theory]
    [InlineData("https://open.api.nexon.com/static/maplestory/item/icon.png", true)]
    [InlineData("https://file.nexon.com/item.png", true)]
    [InlineData("http://open.api.nexon.com/icon.png", false)]
    [InlineData("https://open.api.nexon.com.evil.test/icon.png", false)]
    [InlineData("https://127.0.0.1/icon.png", false)]
    [InlineData("file:///C:/private.png", false)]
    [InlineData("https://open.api.nexon.com:8443/icon.png", false)]
    [InlineData("https://user:secret@open.api.nexon.com/icon.png", false)]
    public void PublicIconsOnlyUseOfficialHttpsHosts(string url, bool valid)
        => Assert.Equal(valid, EquipmentSlots.IsPublicIcon(url));

    [Fact]
    public async Task EquipmentEndpointsSendKeysOnlyInHeadersEscapeOcidAndOmitDate()
    {
        var paths = new List<string>();
        using var handler = new Handler(request =>
        {
            Assert.Equal("?ocid=id%26date%3Dbad", request.RequestUri!.Query);
            Assert.Equal("private-key", Assert.Single(request.Headers.GetValues("x-nxopen-api-key")));
            Assert.DoesNotContain("private-key", request.RequestUri.ToString());
            paths.Add(request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        });
        using var http = new HttpClient(handler); using var api = new NexonApiClient(http);
        await api.GetItemEquipmentAsync("id&date=bad", "private-key");
        await api.GetCashEquipmentAsync("id&date=bad", "private-key");
        await api.GetSymbolEquipmentAsync("id&date=bad", "private-key");
        Assert.Equal(new[] { "/maplestory/v1/character/item-equipment", "/maplestory/v1/character/cashitem-equipment", "/maplestory/v1/character/symbol-equipment" }, paths);
        Assert.Empty(http.DefaultRequestHeaders);
    }

    [Fact]
    public async Task EquipmentCancellationIsHonored()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("A cancelled request must not reach the server."));
        using var http = new HttpClient(handler); using var api = new NexonApiClient(http);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetItemEquipmentAsync("ocid", "key", cancelled.Token));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request));
    }
}
