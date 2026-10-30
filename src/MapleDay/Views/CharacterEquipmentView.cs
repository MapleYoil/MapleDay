using System.Globalization;
using System.Text.Json;
using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace MapleDay.Views;

public sealed class CharacterEquipmentView : UserControl
{
    private readonly StackPanel _root = new() { Spacing = 16, Padding = new Thickness(24) };
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly StackPanel _detail = new() { Spacing = 12, Visibility = Visibility.Collapsed };
    private readonly TextBlock _heading = new() { Text = "캐릭터 정보", FontSize = 28, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _presetStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _boards = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ProgressRing _loading = new() { Width = 24, Height = 24, IsActive = false, Visibility = Visibility.Collapsed };
    private readonly Button _refresh = new() { Content = "새로고침" };
    private readonly ComboBox _look = new() { MinWidth = 150, Visibility = Visibility.Collapsed };
    private readonly EquipmentIconLoader _icons = new();
    private readonly Dictionary<string, Art> _art;
    private readonly Popup _itemPopup = new() { IsLightDismissEnabled = false, IsHitTestVisible = false };
    private FrameworkElement? _hoverOwner;
    private FrameworkElement? _hoverCard;
    private double _hoverHeight;

    private NexonApiClient? _api;
    private string _key = "";
    private SchedulerCharacter[] _characters = [];
    private SchedulerCharacter? _selected;
    private CharacterItemEquipment? _gear;
    private CharacterCashEquipment? _cash;
    private CharacterSymbolEquipment? _symbols;
    private CancellationTokenSource? _request;
    private CancellationTokenSource? _paint;
    private bool _cashMode, _symbolOpen, _coordiOpen, _additional, _extraOpen;
    private int _gearPreset, _cashPreset = 1;
    private SymbolKind _kind = SymbolKind.Arcane;
    private long _generation;
    public bool HasSelection => _selected is not null;
    public event EventHandler? SelectionChanged;

    public CharacterEquipmentView()
    {
        _art = JsonSerializer.Deserialize<List<Art>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Assets", "Equipment", "UIEquip", "manifest.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
            .ToDictionary(art => Path.GetFileNameWithoutExtension(art.File), StringComparer.Ordinal);
        var scroller = new ScrollViewer { Content = _root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroller.ViewChanging += (_, _) => CloseTooltips();
        Content = scroller;
        _root.Children.Add(_heading);
        _root.Children.Add(_list);
        _root.Children.Add(_detail);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var back = new Button { Content = "캐릭터 목록" };
        back.Click += (_, _) => ShowList();
        _refresh.Click += async (_, _) => { if (_selected is not null) await LoadAsync(_selected); };
        actions.Children.Add(back); actions.Children.Add(_refresh); actions.Children.Add(_loading);
        _detail.Children.Add(actions);
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var gear = new Button { Content = "장비" }; var cash = new Button { Content = "캐시 장비" };
        gear.Click += (_, _) => { _cashMode = false; Render(); };
        cash.Click += (_, _) => { _cashMode = true; Render(); };
        modes.Children.Add(gear); modes.Children.Add(cash); modes.Children.Add(_look);
        _look.Items.Add("기본 외형"); _look.Items.Add("추가 외형"); _look.SelectedIndex = 0;
        _look.SelectionChanged += (_, _) => { _additional = _look.SelectedIndex == 1; Render(); };
        _detail.Children.Add(modes); _detail.Children.Add(_status); _detail.Children.Add(_presetStatus);
        _detail.Children.Add(_boards);
        _detail.Children.Add(new TextBlock { Text = "아이템에 마우스를 올리면 정보를 볼 수 있습니다. 중앙 캐릭터는 현재 외형입니다.", TextWrapping = TextWrapping.Wrap });
        Unloaded += (_, _) => CloseTooltips();
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CloseTooltips(); };
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => { if (Visibility != Visibility.Visible) CloseTooltips(); });

        SizeChanged += (_, _) =>
        {
            CloseTooltips(); _boards.Orientation = ActualWidth < (_cashMode ? 810 : 680) ? Orientation.Vertical : Orientation.Horizontal;
        };
    }

    public void SetContext(NexonApiClient api, string key, IEnumerable<SchedulerCharacter> characters)
    {
        var next = characters.ToArray();
        if (_key != key || (_selected is not null && !next.Any(item => item.Ocid == _selected.Ocid))) ShowList();
        if (_key != key) _icons.Clear();
        _api = api; _key = key; _characters = next;
        if (_selected is null) RenderList();
    }

    public void ShowList()
    {
        Cancel(); _selected = null; _gear = null; _cash = null; _symbols = null;
        _heading.Text = "캐릭터 정보"; _list.Visibility = Visibility.Visible; _detail.Visibility = Visibility.Collapsed;
        _boards.Children.Clear(); _status.Text = "";
        RenderList(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop() { ShowList(); _key = ""; _characters = []; _icons.Clear(); _list.Children.Clear(); }
    private void Cancel()
    {
        CloseTooltips();
        _generation++; _request?.Cancel(); _request?.Dispose(); _request = null;
        _paint?.Cancel(); _paint?.Dispose(); _paint = null;
    }

    private void RenderList()
    {
        _list.Children.Clear();
        _list.Children.Add(new TextBlock { Text = _characters.Length == 0 ? "스케줄러에 캐릭터를 추가하면 장비·코디·심볼을 볼 수 있어요." : "스케줄러에 등록한 캐릭터를 선택하세요.", TextWrapping = TextWrapping.Wrap });
        foreach (var character in _characters)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            var portrait = new Image { Width = 64, Height = 76, Stretch = Stretch.Uniform };
            portrait.SetBinding(Image.SourceProperty, new Binding { Source = character.Character, Path = new PropertyPath(nameof(CharacterCard.ImageSource)), Mode = BindingMode.OneWay });
            row.Children.Add(portrait);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
            labels.Children.Add(new TextBlock { Text = character.Name, FontWeight = FontWeights.SemiBold, FontSize = 17 });
            labels.Children.Add(new TextBlock { Text = character.Subtitle, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(labels);
            var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 8, 16, 8) };
            AutomationProperties.SetName(button, character.Name + " 장비 정보 보기");
            button.Click += async (_, _) => await LoadAsync(character);
            _list.Children.Add(button);
        }
    }

    private async Task LoadAsync(SchedulerCharacter character)
    {
        if (_api is null || string.IsNullOrWhiteSpace(_key)) return;
        Cancel(); var generation = _generation;
        _request = new CancellationTokenSource(); var token = _request.Token;
        _selected = character; _gear = null; _cash = null; _symbols = null;
        _gearPreset = 0; _cashPreset = 1; _additional = false; _look.SelectedIndex = 0;
        _extraOpen = false;
        _heading.Text = character.Name + " · 캐릭터 정보";
        _list.Visibility = Visibility.Collapsed; _detail.Visibility = Visibility.Visible;
        _loading.IsActive = true; _loading.Visibility = Visibility.Visible; _refresh.IsEnabled = false;
        _status.Text = "장비·캐시 장비·심볼을 불러오는 중…";
        Render(); SelectionChanged?.Invoke(this, EventArgs.Empty);
        var errors = new List<string>();
        async Task Fetch<T>(string label, Func<Task<T>> fetch, Action<T> assign)
        {
            try { var result = await fetch(); if (generation == _generation && !token.IsCancellationRequested) assign(result); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) when (ex is NexonApiException or HttpRequestException or TaskCanceledException)
            { if (generation == _generation) errors.Add(label + ": " + (ex is NexonApiException ? ex.Message : "연결하지 못했어요. 새로고침으로 다시 시도해주세요.")); }
        }
        await Task.WhenAll(
            Fetch("장비", () => _api.GetItemEquipmentAsync(character.Ocid, _key, token), result => _gear = result),
            Fetch("캐시 장비", () => _api.GetCashEquipmentAsync(character.Ocid, _key, token), result => _cash = result),
            Fetch("심볼", () => _api.GetSymbolEquipmentAsync(character.Ocid, _key, token), result => _symbols = result));
        if (generation != _generation || token.IsCancellationRequested) return;
        _loading.IsActive = false; _loading.Visibility = Visibility.Collapsed; _refresh.IsEnabled = true;
        _status.Text = errors.Count > 0 ? string.Join("\n", errors) : "조회한 장비 정보입니다. 프리셋 선택은 미리보기입니다.";
        Render();
    }

    private void Render()
    {
        CloseTooltips();
        _paint?.Cancel(); _paint?.Dispose(); _paint = new CancellationTokenSource();
        _boards.Children.Clear();
        if (_selected is null) return;
        _look.Visibility = _cashMode && _cash?.AdditionalBase is not null ? Visibility.Visible : Visibility.Collapsed;
        _boards.Orientation = ActualWidth < (_cashMode ? 810 : 680) ? Orientation.Vertical : Orientation.Horizontal;
        _presetStatus.Text = _cashMode ? "현재 코디와 선택한 코디 프리셋을 비교할 수 있어요." :
            $"{(_gearPreset == 0 ? "현재 장착" : $"장비 프리셋 {_gearPreset}")} · 적용 중인 프리셋: {_gear?.PresetNo?.ToString() ?? "정보 없음"}";
        var board = new Canvas { Width = 366, Height = 444 };
        AddArt(board, _cashMode ? "Deco_main_backgrnd" : "Equip_main_backgrnd");
        AddArt(board, _cashMode ? "Deco_main_tab_detailTab_selected_0" : "Equip_main_tab_detailTab_selected_0");
        if (_cashMode)
        {
            AddArt(board, "Deco_main_tab_detailTab_normal_1"); AddArt(board, "Deco_main_tab_detailTab_normal_2");
            AddArt(board, "Deco_CoordiTab_canvas_coordi"); AddAvatar(board, 117, 100);
            AddCashSlots(board, "Deco_CoordiTab", _cash?.Select(0, _additional));
            ArtButton(board, "Deco_CoordiTab_button_EquipUI_normal_0", "장비 보기", () => { _cashMode = false; Render(); });
            ArtButton(board, "Deco_CoordiTab_button_CoordiPreset_normal_0", _coordiOpen ? "코디 프리셋 접기" : "코디 프리셋 펼치기", () => { _coordiOpen = !_coordiOpen; Render(); });
            _boards.Children.Add(Wrap(board));
            if (_coordiOpen) AddCashPreset();
        }
        else
        {
            AddArt(board, "Equip_main_tab_detailTab_normal_1");
            AddArt(board, "Equip_EquipTab_canvas_equip"); AddAvatar(board, 117, 100);
            var items = _gear?.Select(_gearPreset);
            foreach (var pair in EquipmentSlots.Gear.Where(pair => pair.Key != "32"))
            {
                var asset = "Equip_EquipTab_Slots_" + pair.Key;
                if (!_art.TryGetValue(asset, out var position)) continue;
                var item = items?.FirstOrDefault(item => EquipmentSlots.Matches(item.Slot, pair.Value));
                if (pair.Key == "title")
                    Slot(board, position, "Equip_EquipTab_SlotName_title", _gear?.Title?.Name, _gear?.Title?.Icon,
                        () => { },
                        details: () => EquipmentTooltipCard.Simple(_gear?.Title?.Name, _gear?.Title?.Icon, LoadTooltipIcon, [_gear?.Title?.Description]));
                else Slot(board, position, "Equip_EquipTab_SlotName_" + pair.Key, item?.Name, item?.Icon,
                    () => { }, pair.Value, item);
            }
            var selectedPreset = _gearPreset == 0 ? _gear?.PresetNo : _gearPreset;
            for (var i = 1; i <= 3; i++)
            {
                var preset = i;
                var selected = selectedPreset == i;
                ArtButton(board, $"Equip_EquipTab_button_preset{i}_{(selected ? "checked" : "normal")}_0",
                    $"장비 프리셋 {i} 보기{(selected ? ", 현재 선택됨" : "")}", () => { _gearPreset = preset; Render(); });
            }
            var current = new Button { Content = "현재", FontSize = 11, Padding = new Thickness(6, 0, 6, 0), Height = 24, MinHeight = 0 };
            current.Click += (_, _) => { _gearPreset = 0; Render(); }; Place(board, current, 282, 376);
            ArtButton(board, "Equip_EquipTab_button_DecoUI_normal_0", "캐시 장비 보기", () => { _cashMode = true; Render(); });
            ArtButton(board, "Equip_EquipTab_button_symbol_normal_0", _symbolOpen ? "심볼 접기" : "심볼 펼치기", () => { _symbolOpen = !_symbolOpen; Render(); });
            var special = _gear?.Dragon is { Count: > 0 } ? "dragon" : _gear?.Mechanic is { Count: > 0 } ? "mechanic" : null;
            if (special is not null) ArtButton(board, $"Equip_EquipTab_button_{special}EquipOpen_normal_0", "직업 전용 장비 펼치기 / 접기", () => { _extraOpen = !_extraOpen; Render(); });
            _boards.Children.Add(Wrap(board));
            if (_symbolOpen) AddSymbols();
            if (_extraOpen) AddExtraEquipment();
            if (_gear is not null && items is null) _presetStatus.Text += " · 이 프리셋의 정보가 제공되지 않았습니다.";
            else if (items?.Count == 0) _presetStatus.Text += " · 빈 프리셋입니다.";
        }
    }

    private void AddAvatar(Canvas board, double x, double y)
    {
        var background = new Border { Width = 132, Height = 177, Background = new SolidColorBrush(Colors.White), CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
        Place(board, background, x, y);
        var avatar = new Image { Width = 112, Height = 135, Stretch = Stretch.Uniform };
        if (_selected is not null) avatar.SetBinding(Image.SourceProperty, new Binding { Source = _selected.Character, Path = new PropertyPath(nameof(CharacterCard.ImageSource)), Mode = BindingMode.OneWay });
        Place(board, avatar, x + 10, y + 35);
    }

    private void AddCashSlots(Canvas board, string prefix, IReadOnlyList<CashEquipmentItem>? items)
    {
        foreach (var pair in EquipmentSlots.Gear.Where(pair => int.TryParse(pair.Key, out var id) && id < 17))
        {
            var id = int.Parse(pair.Key, CultureInfo.InvariantCulture) + 100;
            if (!_art.TryGetValue(prefix + "_Slots_" + id, out var position)) continue;
            var item = items?.FirstOrDefault(item => EquipmentSlots.Matches(item.Slot ?? item.Part, pair.Value));
            Slot(board, position, "Equip_EquipTab_SlotName_" + pair.Key, item?.Name, item?.Icon,
                () => { }, pair.Value,
                details: item is null ? null : () => EquipmentTooltipCard.Cash(item, LoadTooltipIcon));
        }
    }

    private void AddCashPreset()
    {
        var board = new Canvas { Width = 358, Height = 352 };
        AddArt(board, "Deco_CoordiPreset_backgrnd"); AddArt(board, "Deco_CoordiPreset_canvas_coordiPreset");
        AddAvatar(board, 113, 47); AddCashSlots(board, "Deco_CoordiPreset", _cash?.Select(_cashPreset, _additional));
        for (var i = 1; i <= 3; i++)
        {
            var preset = i;
            ArtButton(board, $"Deco_CoordiPreset_button_preset{i}_{(_cashPreset == i ? "checked" : "normal")}_0", $"코디 프리셋 {i} 보기", () => { _cashPreset = preset; Render(); });
        }
        _boards.Children.Add(Wrap(board));
        _presetStatus.Text = $"현재 코디 · 코디 프리셋 {_cashPreset} 미리보기 · 적용 중: {_cash?.PresetNo?.ToString() ?? "정보 없음"}";
        if (_cash is not null && _cash.Select(_cashPreset, _additional) is null) _presetStatus.Text += " · 프리셋 정보 없음";
    }

    private void AddExtraEquipment()
    {
        foreach (var (kind, items, slots) in new[]
        {
            ("dragon", _gear?.Dragon, new[] { (1000, "드래곤 모자"), (1001, "드래곤 펜던트"), (1002, "드래곤 날개장식"), (1003, "드래곤 꼬리장식") }),
            ("mechanic", _gear?.Mechanic, new[] { (1100, "메카닉 엔진"), (1101, "메카닉 암"), (1102, "메카닉 다리"), (1103, "메카닉 프레임"), (1104, "메카닉 트랜지스터") })
        })
        {
            if (items is not { Count: > 0 }) continue;
            var prefix = $"Equip_EquipTab_{kind}Equip";
            var asset = _art[$"{prefix}_canvas_{kind}"];
            var board = new Canvas { Width = asset.Width, Height = asset.Height, Margin = new Thickness(0, 53, 0, 0) };
            AddArt(board, $"{prefix}_canvas_{kind}");
            foreach (var (id, name) in slots)
            {
                var item = items.FirstOrDefault(item => EquipmentSlots.Matches(item.Slot, name));
                Slot(board, _art[$"{prefix}_Slots_{id}"], $"{prefix}_SlotName_{id}", item?.Name, item?.Icon, () => { }, name, item);
            }
            _boards.Children.Add(Wrap(board));
        }
    }

    private void AddSymbols()
    {
        var board = new Canvas { Width = 228, Height = 339, Margin = new Thickness(0, 53, 0, 0) };
        AddArt(board, "Equip_Symbol_backgrnd");
        for (var i = 0; i < 2; i++)
        {
            var arcane = i == 0; var active = arcane == (_kind == SymbolKind.Arcane);
            // The tab art origin is relative to the category tab at (15,-18).
            var asset = $"Equip_Symbol_tab_categoryTab_{(active ? "selected" : "normal")}_{i}";
            ArtButton(board, asset, arcane ? "아케인 심볼" : "어센틱 심볼", () => { _kind = arcane ? SymbolKind.Arcane : SymbolKind.Authentic; Render(); }, 22 + i * 92, 12);
        }
        var type = _kind switch { SymbolKind.Arcane => "ArcEquip", SymbolKind.Authentic => "AutEquip", _ => "GrandAutEquip" };
        var prefix = "Equip_Symbol_" + type;
        AddArt(board, prefix + "_backgrnd", 22, 46);
        if (_kind != SymbolKind.Arcane)
            ArtButton(board, prefix + "_button_next_normal_0", _kind == SymbolKind.Authentic ? "그랜드 어센틱 심볼 보기" : "어센틱 심볼 보기",
                () => { _kind = _kind == SymbolKind.Authentic ? SymbolKind.GrandAuthentic : SymbolKind.Authentic; Render(); }, 176, 53);
        var symbols = (_symbols?.Symbols ?? []).Where(symbol => symbol.Kind == _kind).ToArray();
        var totals = new List<(string Key, decimal Value)>
        { ("force", _symbols?.TotalForce(_kind) ?? 0) };
        if (_kind == SymbolKind.GrandAuthentic)
            totals.AddRange(new[] { ("exp", symbols.Sum(s => EquipmentSymbol.Number(s.Exp))), ("meso", symbols.Sum(s => EquipmentSymbol.Number(s.Meso))), ("drop", symbols.Sum(s => EquipmentSymbol.Number(s.Drop))) });
        else
            totals.AddRange(new[] { ("str", symbols.Sum(s => EquipmentSymbol.Number(s.Str))), ("dex", symbols.Sum(s => EquipmentSymbol.Number(s.Dex))), ("int", symbols.Sum(s => EquipmentSymbol.Number(s.Int))), ("luk", symbols.Sum(s => EquipmentSymbol.Number(s.Luk))), ("hp", symbols.Sum(s => EquipmentSymbol.Number(s.Hp))) }.Where(pair => pair.Item2 > 0));
        var start = _kind == SymbolKind.GrandAuthentic ? 94 : totals.Count > 2 ? 71 : 79;
        var gap = totals.Count > 2 && _kind != SymbolKind.GrandAuthentic ? 13 : 20;
        for (var i = 0; i < totals.Count; i++)
        {
            var (label, value) = totals[i]; AddArt(board, prefix + "_textUp_" + label, 75, start + i * gap);
            Glyphs(board, prefix + "_textUp_", "+" + value.ToString("0.##", CultureInfo.InvariantCulture) + (_kind == SymbolKind.GrandAuthentic && label != "force" ? "%" : ""), 120, start + i * gap);
        }
        var slots = _symbols?.Slots(_kind) ?? new EquipmentSymbol?[_kind == SymbolKind.GrandAuthentic ? 3 : 6];
        for (var i = 0; i < slots.Count; i++)
        {
            var symbol = slots[i];
            var x = 22 + (_kind == SymbolKind.Arcane ? 18 : 20) + (i % 3) * 50;
            var y = 46 + (_kind == SymbolKind.GrandAuthentic ? 148 : 79 + (i / 3) * 92);
            AddArt(board, prefix + (symbol is null ? "_disablebox" : "_box"), x, y);
            if (symbol is null) continue;
            var image = new Image { Width = 36, Height = 38, Stretch = Stretch.Uniform };
            var button = FlatButton(image, symbol.Name ?? "심볼", () => { });
            DetailTooltip(button, () => EquipmentTooltipCard.Symbol(symbol, LoadTooltipIcon));
            button.Width = 40; button.Height = 43; Place(board, button, x + 2, y + 3); _ = LoadIconAsync(image, symbol.Icon, _paint!.Token);
            Glyphs(board, prefix + "_textDown_", "v" + symbol.Level, x + 5, y + 46);
            if (_kind == SymbolKind.Arcane && symbol.Level >= 20) AddArt(board, prefix + "_base2", x + 10, y + 61);
            else
            {
                AddArt(board, prefix + "_button_enchant_normal_0", x + (_kind == SymbolKind.Arcane ? 4 : 0), y + 60);
                var progress = symbol.RequiredGrowth > 0 ? Math.Clamp((double)symbol.Growth / symbol.RequiredGrowth, 0, 1) : 0;
                if (progress > 0 && AddArt(board, prefix + "_canvas_gauge", x + (_kind == SymbolKind.Arcane ? 12 : 10), y + 80) is { } gauge)
                    gauge.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 24 * progress, 3) };
            }
        }
        _boards.Children.Add(Wrap(board));
        if (_symbols is null) _presetStatus.Text += " · 심볼 정보 없음";
    }

    private void Glyphs(Canvas board, string prefix, string value, double x, double y)
    {
        foreach (var character in value)
        {
            var asset = prefix + (character switch { '+' => "plus", '%' => "percent", '.' => "dot", _ => character.ToString() });
            if (_art.TryGetValue(asset, out var art)) { AddArt(board, asset, x, y); x += art.Width + 1; }
            else { var text = new TextBlock { Text = character.ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Colors.White) }; Place(board, text, x, y - 3); x += 8; }
        }
    }

    private void Slot(Canvas board, Art position, string label, string? name, string? icon, Action selected, string? emptyName = null,
        EquipmentItem? equipment = null, Func<FrameworkElement>? details = null)
    {
        var x = -position.OriginX; var y = -position.OriginY;
        AddArt(board, Path.GetFileNameWithoutExtension(position.File), x, y);
        if (string.IsNullOrEmpty(name)) { AddArt(board, label, x, y); return; }
        var image = new Image { Width = 34, Height = 34, Stretch = Stretch.Uniform };
        var content = new Grid { Width = 42, Height = 42 };
        content.Children.Add(image);
        if (equipment is not null)
        {
            DecorateEquipment(content, equipment);
            details = () => EquipmentTooltipCard.Gear(equipment, LoadTooltipIcon, _gearPreset == 0 || _gearPreset == _gear?.PresetNo);
        }
        var button = FlatButton(content, (emptyName is null ? "" : emptyName + " · ") + name, selected);
        if (details is not null) DetailTooltip(button, details);
        button.Width = 42; button.Height = 42; Place(board, button, x, y);
        _ = LoadIconAsync(image, icon, _paint!.Token);
    }

    private async Task LoadIconAsync(Image image, string? url, CancellationToken token)
    {
        try { var bitmap = await _icons.LoadAsync(url, token); if (!token.IsCancellationRequested) image.Source = bitmap; }
        catch (Exception error) when (error is OperationCanceledException or HttpRequestException or IOException or System.Runtime.InteropServices.COMException or ArgumentException) { }
    }

    private void LoadTooltipIcon(Image image, string? url)
    {
        if (_paint is not null) _ = LoadIconAsync(image, url, _paint.Token);
    }

    private void DetailTooltip(Button button, Func<FrameworkElement> create)
    {
        // Open on pointer entry instead of depending on ToolTipService's delayed hover lifecycle.
        ToolTipService.SetToolTip(button, null);
        button.PointerEntered += (_, args) =>
        {
            if (XamlRoot?.Content is not UIElement root) return;
            OpenItemPopup(button, create, args.GetCurrentPoint(root).Position);
        };
        button.PointerMoved += (_, args) =>
        {
            if (XamlRoot?.Content is UIElement root && _hoverOwner == button)
                PositionItemPopup(args.GetCurrentPoint(root).Position);
        };
        button.PointerExited += (_, _) => { if (_hoverOwner == button) CloseTooltips(); };
        button.PointerCanceled += (_, _) => { if (_hoverOwner == button) CloseTooltips(); };
        button.GotFocus += (_, _) =>
        {
            if (button.FocusState != FocusState.Keyboard || XamlRoot?.Content is not UIElement root) return;
            var point = button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(button.ActualWidth, 16));
            OpenItemPopup(button, create, point);
        };
        button.LostFocus += (_, _) => { if (_hoverOwner == button) CloseTooltips(); };
        button.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Escape) { CloseTooltips(); args.Handled = true; }
            else if (_hoverOwner == button && _itemPopup.Child is ScrollViewer scroller && args.Key is Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Up)
            {
                scroller.ChangeView(null, Math.Clamp(scroller.VerticalOffset + (args.Key == Windows.System.VirtualKey.Down ? 32 : -32), 0, scroller.ScrollableHeight), null, true);
                args.Handled = true;
            }
        };
        button.AddHandler(PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
        {
            if (_hoverOwner != button || _itemPopup.Child is not ScrollViewer scroller || scroller.ScrollableHeight <= 0) return;
            var delta = args.GetCurrentPoint(button).Properties.MouseWheelDelta;
            scroller.ChangeView(null, Math.Clamp(scroller.VerticalOffset - delta / 120.0 * 48, 0, scroller.ScrollableHeight), null, true);
            args.Handled = true;
        }), true);
    }

    private void OpenItemPopup(FrameworkElement owner, Func<FrameworkElement> create, Windows.Foundation.Point point)
    {
        if (XamlRoot is null || Visibility != Visibility.Visible) return;
        CloseTooltips();
        _hoverOwner = owner; _hoverCard = create();
        _hoverCard.Measure(new Windows.Foundation.Size(EquipmentTooltipLayout.Width, double.PositiveInfinity));
        _hoverHeight = Math.Max(42, _hoverCard.DesiredSize.Height);
        _itemPopup.XamlRoot = XamlRoot;
        _itemPopup.Child = new ScrollViewer { Content = _hoverCard, HorizontalScrollMode = ScrollMode.Auto, VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        PositionItemPopup(point); _itemPopup.IsOpen = true;
    }

    private void PositionItemPopup(Windows.Foundation.Point point)
    {
        if (XamlRoot is null || _itemPopup.Child is not ScrollViewer view) return;
        var position = EquipmentTooltipLayout.Place(point.X, point.Y, _hoverHeight, XamlRoot.Size.Width, XamlRoot.Size.Height);
        view.Width = position.Width; view.Height = position.Height;
        _itemPopup.HorizontalOffset = position.X; _itemPopup.VerticalOffset = position.Y;
    }

    private void CloseTooltips()
    {
        _itemPopup.IsOpen = false; _itemPopup.Child = null; _hoverOwner = null; _hoverCard = null;
    }

    private static void DecorateEquipment(Grid content, EquipmentItem item)
    {
        if (EquipmentDetails.GradeKey(item.Grade) != "normal")
        {
            content.Children.Add(new Border { BorderBrush = new SolidColorBrush(EquipmentTooltipCard.GradeColor(item.Grade)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2), IsHitTestVisible = false });
            var grade = EquipmentTooltipCard.Sprite("textIcon_potential_title_" + EquipmentDetails.GradeKey(item.Grade), 10, 10);
            grade.HorizontalAlignment = HorizontalAlignment.Right; grade.VerticalAlignment = VerticalAlignment.Bottom; grade.Margin = new Thickness(0, 0, 2, 2);
            content.Children.Add(grade);
        }
        if (EquipmentDetails.GradeKey(item.AdditionalGrade) != "normal")
        {
            var grade = EquipmentTooltipCard.Sprite("textIcon_additionalPotential_title_" + EquipmentDetails.GradeKey(item.AdditionalGrade), 10, 10);
            grade.HorizontalAlignment = HorizontalAlignment.Right; grade.VerticalAlignment = VerticalAlignment.Bottom; grade.Margin = new Thickness(0, 0, 2, 13);
            content.Children.Add(grade);
        }
        var stars = EquipmentDetails.Stars(item);
        if (stars > 0) content.Children.Add(SmallBadge("★" + stars, VerticalAlignment.Top));
        if (item.SpecialRingLevel > 0) content.Children.Add(SmallBadge("Lv." + item.SpecialRingLevel, VerticalAlignment.Bottom));
    }

    private static Border SmallBadge(string label, VerticalAlignment alignment) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = alignment, Margin = new Thickness(1), Padding = new Thickness(1, 0, 1, 0),
        Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(220, 35, 43, 52)), CornerRadius = new CornerRadius(2), IsHitTestVisible = false,
        Child = EquipmentTooltipCard.OriginalText(label, "starForce/normal")
    };

    private Image? AddArt(Canvas board, string asset, double? x = null, double? y = null)
    {
        if (!_art.TryGetValue(asset, out var art)) return null;
        var image = new Image { Width = art.Width, Height = art.Height, Source = LocalImageCache.Get("Equipment/UIEquip/" + art.File), IsHitTestVisible = false, Stretch = Stretch.Fill };
        Place(board, image, x ?? -art.OriginX, y ?? -art.OriginY); return image;
    }
    private void ArtButton(Canvas board, string asset, string label, Action clicked, double? x = null, double? y = null)
    {
        if (!_art.TryGetValue(asset, out var art)) return;
        var image = new Image { Source = LocalImageCache.Get("Equipment/UIEquip/" + art.File), Width = art.Width, Height = art.Height, Stretch = Stretch.Fill };
        var button = FlatButton(image, label, clicked); button.Width = art.Width; button.Height = art.Height;
        // Use WinUI's real interaction states so pointer capture, cancel and keyboard activation stay in sync.
        if (asset.EndsWith("_normal_0", StringComparison.Ordinal))
        {
            var prefix = asset[..^"_normal_0".Length];
            string State(string state) => _art.ContainsKey(prefix + "_" + state + "_0") ? prefix + "_" + state + "_0" : asset;
            void UpdateImage()
            {
                var state = !button.IsEnabled ? "disabled" : button.IsPressed ? "pressed" : button.IsPointerOver ? "mouseOver" : "normal";
                var current = _art[State(state)];
                image.Source = LocalImageCache.Get("Equipment/UIEquip/" + current.File);
                image.RenderTransform = new TranslateTransform { X = art.OriginX - current.OriginX, Y = art.OriginY - current.OriginY };
            }
            button.RegisterPropertyChangedCallback(Button.IsPressedProperty, (_, _) => UpdateImage());
            button.RegisterPropertyChangedCallback(Button.IsPointerOverProperty, (_, _) => UpdateImage());
            button.IsEnabledChanged += (_, _) => UpdateImage();
        }
        Place(board, button, x ?? -art.OriginX, y ?? -art.OriginY);
    }
    private static Button FlatButton(object content, string label, Action clicked)
    {
        var button = new Button { Content = content, Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0), MinWidth = 0, MinHeight = 0 };
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" }) button.Resources[key] = new SolidColorBrush(Colors.Transparent);
        AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label); button.Click += (_, _) => clicked(); return button;
    }
    private static Viewbox Wrap(Canvas canvas) => new() { Child = canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = canvas.Width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private static void Place(Canvas board, UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); board.Children.Add(element); }
    private sealed record Art(string File, int Width, int Height, int OriginX, int OriginY);
}
