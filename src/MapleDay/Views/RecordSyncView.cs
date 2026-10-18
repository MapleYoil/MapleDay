using System.Security.Cryptography;
using System.Text.Json;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace MapleDay.Views;

public sealed class RecordSyncView : UserControl
{
    private readonly RecordSyncClient _client = new();
    private readonly StackPanel _root = new() { Spacing = 12 };
    private readonly TextBlock _status = new() { Text = "연결하지 않음", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _name = new() { Header = "이 기기 이름", Text = "이 PC", MaxLength = 50 };
    private readonly PasswordBox _code = new() { Header = "다른 PC의 연결 코드 또는 복구 코드" };
    private readonly StackPanel _offline = new() { Spacing = 10 };
    private readonly StackPanel _online = new() { Spacing = 10, Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromSeconds(2) };
    private AppSettings? _settings;
    private CancellationTokenSource _life = new();
    private Task? _watch;
    private Task? _operation;
    private bool _stopped, _busy, _syncRequested;
    public event EventHandler? Changed;

    public RecordSyncView()
    {
        Content = _root;
        _root.Children.Add(new TextBlock { Text = "PC 간 기록 동기화", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _root.Children.Add(new TextBlock { Text = "사냥·보스·물욕템 기록, 캘린더, 파티와 스케줄러 등록 목록을 다른 PC와 연결합니다. API 키와 조회 캐시는 각 PC에서 따로 설정하세요. 기록은 기기에서 암호화하며 서버에 복호화 키를 보내지 않습니다.", TextWrapping = TextWrapping.Wrap });
        _root.Children.Add(_status); _root.Children.Add(_offline); _root.Children.Add(_online);
        _offline.Children.Add(_name); _offline.Children.Add(_code);
        Add(_offline, "새 동기화 만들기", async () => await ConnectAsync(""));
        Add(_offline, "코드로 연결", async () => { if (string.IsNullOrWhiteSpace(_code.Password)) throw new ArgumentException("연결 코드를 입력해주세요."); await ConnectAsync(_code.Password); });
        Add(_online, "지금 동기화", () => SynchronizeAsync(true));
        Add(_online, "다른 PC 연결 코드", async () => await ShowCodeAsync("5분 동안 한 번 사용할 수 있는 연결 코드", await _client.PairAsync(_life.Token)));
        Add(_online, "복구 코드 확인", async () =>
        {
            if (_client.RecoveryCode.Length == 0) throw new ArgumentException("새 동기화를 만든 PC 또는 복구 코드로 연결한 PC에서 확인할 수 있어요.");
            await ShowCodeAsync("복구 코드를 안전하게 보관해주세요", _client.RecoveryCode);
        });
        Add(_online, "연결 기기 관리", DevicesAsync);
        Add(_online, "동기화 전 백업 복원", RestoreAsync);
        Add(_online, "이 PC 연결 해제", async () =>
        {
            if (await DialogAsync("이 PC 연결 해제", "이 PC의 기록은 유지합니다. 서버의 기기 권한을 취소하고 자동 동기화를 중지합니다.", "연결 해제") != ContentDialogResult.Primary) return;
            var remoteRevoked = true;
            try { await _client.RevokeAsync(_client.Checkpoint!.Credentials.Device, _life.Token); }
            catch (Exception error) when (Expected(error)) { remoteRevoked = false; }
            _client.Disconnect(); CancelWatch(); UpdateState(); _status.Text = remoteRevoked ? "연결을 해제했어요. 이 PC의 기록은 유지됩니다."
                : "이 PC의 연결을 해제했어요. 서버에 연결하지 못했으므로 다른 PC의 기기 관리에서 이 기기의 권한도 취소해주세요.";
        });
        Add(_online, "서버 동기화 데이터 삭제", async () =>
        {
            var text = new TextBox { Header = "‘삭제’를 정확히 입력하세요", PlaceholderText = "삭제" };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "모든 PC의 동기화 연결과 서버에 저장된 암호화 기록을 삭제합니다. 각 PC에 저장된 기록은 유지됩니다. 복구 권한이 있는 PC에서만 삭제할 수 있습니다.", TextWrapping = TextWrapping.Wrap }); panel.Children.Add(text);
            var dialog = MakeDialog("서버 동기화 데이터 삭제", panel, "데이터 삭제"); dialog.IsPrimaryButtonEnabled = false;
            text.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = text.Text == "삭제";
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || text.Text != "삭제") return;
            await _client.DeleteAsync(text.Text, _life.Token); CancelWatch(); UpdateState(); _status.Text = "서버 동기화 데이터와 기기 연결을 삭제했어요.";
        }, danger: true);
        _poll.Tick += (_, _) => ScheduleSync();
        _debounce.Tick += (_, _) => { _debounce.Stop(); ScheduleSync(); };
    }

    private void Add(StackPanel parent, string label, Func<Task> action, bool danger = false)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        if (danger) button.Foreground = AppTheme.Brush("DangerTextBrush");
        button.Click += (_, _) => Run(action); parent.Children.Add(button);
    }
    public async Task InitializeAsync(AppSettings settings)
    {
        if (_settings is not null || _stopped) return;
        _settings = settings; AppSettings.Saved += OnSaved;
        try { await _client.LoadAsync(); if (_stopped) return; UpdateState(); StartWatch(); }
        catch (Exception error) when (Expected(error)) { _status.Text = "저장된 동기화 연결을 읽지 못했어요. 로컬 기록은 유지됩니다."; }
    }
    private void UpdateState()
    {
        var connected = _client.Checkpoint is not null;
        _online.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        _offline.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        _status.Text = connected ? (_client.Checkpoint!.Confirmed ? "연결됨 · 기록 변경 시 자동 동기화" : "연결됨 · ‘지금 동기화’에서 초기 병합을 확인해주세요.") : "연결하지 않음";
    }
    private void OnSaved(AppSettings settings)
    {
        if (_stopped || _client.Applying || settings != _settings) return;
        DispatcherQueue.TryEnqueue(() => { if (_stopped) return; _debounce.Stop(); _debounce.Start(); });
    }
    private void ScheduleSync()
    {
        if (_stopped || _client.Checkpoint?.Confirmed != true) return;
        if (_busy) { _syncRequested = true; return; }
        Run(() => SynchronizeAsync(false));
    }
    private void Run(Func<Task> action)
    {
        if (_busy || _stopped) return;
        _operation = RunAsync(action);
    }
    private async Task RunAsync(Func<Task> action)
    {
        _busy = true; SetEnabled(false);
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (Expected(error))
        {
            if (!_stopped) _status.Text = error is ArgumentException ? error.Message : "동기화하지 못했어요. 로컬 기록은 유지됩니다. 연결 상태와 복구 코드를 확인한 뒤 다시 시도해주세요.";
        }
        finally
        {
            _busy = false;
            if (!_stopped) { SetEnabled(true); if (_syncRequested) { _syncRequested = false; _debounce.Stop(); _debounce.Start(); } }
        }
    }
    private void SetEnabled(bool enabled)
    { foreach (var control in _online.Children.Concat(_offline.Children).OfType<Control>()) control.IsEnabled = enabled; }
    private static bool Expected(Exception error) => error is ArgumentException or IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException
        or JsonException or CryptographicException or InvalidOperationException or FormatException or System.Runtime.InteropServices.COMException;
    private async Task ConnectAsync(string code)
    {
        if (_life.IsCancellationRequested) { if (_watch is not null) await _watch; _life.Dispose(); _life = new(); }
        await _client.ConnectAsync(code, _name.Text, _life.Token); _code.Password = "";
        UpdateState(); StartWatch(); await SynchronizeAsync(true);
    }
    private void StartWatch()
    {
        if (_stopped || _client.Checkpoint is null || _watch is { IsCompleted: false }) return;
        _poll.Start();
        _watch = _client.WatchAsync(() => DispatcherQueue.TryEnqueue(ScheduleSync), _life.Token);
    }
    private void CancelWatch() { _poll.Stop(); _debounce.Stop(); _life.Cancel(); }
    private async Task SynchronizeAsync(bool interactive)
    {
        if (_settings is null || _stopped) return;
        var before = SyncRecords.Capture(_settings);
        var result = await _client.SynchronizeAsync(_settings,
            async (local, remote) => interactive && await DialogAsync("처음 연결한 기록 병합", $"이 PC {local:N0}개 · 서버 {remote:N0}개 기록을 비교합니다. 다른 기록은 합치고, 충돌은 직접 선택합니다. 적용 전 기록은 이 PC에 암호화 백업하며 최대 10개를 보관합니다.\n\nAPI 키·개인 설정·문의 내용은 전송하지 않습니다.", "비교하고 동기화") == ContentDialogResult.Primary,
            conflicts => interactive ? ResolveAsync(conflicts) : Task.FromResult<IReadOnlySet<string>?>(null), _life.Token);
        if (_stopped) return;
        if (result.Length > 0) _status.Text = result;
        if (!SyncRecords.Same(before, SyncRecords.Capture(_settings))) Changed?.Invoke(this, EventArgs.Empty);
    }
    private async Task<IReadOnlySet<string>?> ResolveAsync(IReadOnlyList<SyncConflict> conflicts)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var conflict in conflicts)
        {
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "양쪽 PC에서 같은 기록을 변경했습니다. 이번에 유지할 내용을 선택하세요. 취소하면 전체 동기화를 보류합니다.", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "이 PC\n" + Describe(conflict.Local), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "다른 PC\n" + Describe(conflict.Remote), TextWrapping = TextWrapping.Wrap });
            var dialog = MakeDialog($"기록 충돌 {++index}/{conflicts.Count}", panel, "이 PC 유지"); dialog.SecondaryButtonText = "다른 PC 유지";
            var answer = await dialog.ShowAsync();
            if (answer == ContentDialogResult.None) return null;
            if (answer == ContentDialogResult.Primary) selected.Add(conflict.Key);
        }
        return selected;
    }
    private static string Describe(JsonElement? element)
    {
        if (element is null) return "삭제된 기록";
        static string Read(JsonElement value, string key) => value.TryGetProperty(key, out var found) ? found.ToString() : "";
        var row = element.Value;
        if (row.ValueKind != JsonValueKind.Object) return row.ToString();
        var parts = new List<string>();
        foreach (var (key, label) in new[] { ("Date", "날짜"), ("Name", "이름"), ("Title", "제목"), ("Boss", "보스"), ("Difficulty", "난이도"),
            ("ItemName", "아이템"), ("Meso", "획득 메소"), ("Fragments", "조각 수"), ("FragmentUnitPrice", "조각 단가"), ("Amount", "금액"), ("PartySize", "인원"),
            ("Ratios", "분배 비율"), ("OwnMember", "내 순번"), ("Time", "시간"), ("Weekly", "매주 반복"), ("RepeatUntil", "반복 종료일"), ("Note", "메모") })
            if (Read(row, key) is { Length: > 0 } text) parts.Add(label + ": " + text);
        if (row.TryGetProperty("Members", out var members) && members.ValueKind == JsonValueKind.Array)
            parts.Add("파티 구성원: " + string.Join(", ", members.EnumerateArray().Select(item => Read(item, "Name") + " (비율 " + Read(item, "Weight") + ")")));
        if (row.TryGetProperty("Settlement", out var settlement) && settlement.ValueKind == JsonValueKind.Object)
            parts.Add("정산 상태: " + (Read(settlement, "State") switch { "complete" => "정산 완료", "partial" => "부분 수령", _ => "미정산" }) + " · 판매 수수료 " + Read(settlement, "AuctionFee") + "% · 실제 수령 " + Read(settlement, "ActualReceived"));
        if (row.TryGetProperty("Replacement", out var replacement)) parts.Add("수정 기록: " + (replacement.ValueKind == JsonValueKind.Null ? "처치 기록 삭제" : Describe(replacement)));
        if (row.TryGetProperty("Value", out var size)) parts.Add("인원: " + size);
        return parts.Count > 0 ? string.Join("\n", parts) : "스케줄러 등록 정보 변경";
    }
    private ContentDialog MakeDialog(string title, object content, string primary = "") => new()
    {
        XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = title,
        Content = content is string text ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } : new ScrollViewer { Content = content, MaxHeight = 400 },
        PrimaryButtonText = primary, CloseButtonText = "닫기", DefaultButton = ContentDialogButton.Close
    };
    private Task<ContentDialogResult> DialogAsync(string title, string text, string primary) => MakeDialog(title, text, primary).ShowAsync().AsTask();
    private async Task ShowCodeAsync(string title, string code)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "이 코드로 기록을 복호화하고 연결할 수 있습니다. 신뢰하는 본인의 기기에만 입력하세요. 복구 코드를 잃으면 모든 PC를 잃었을 때 복원할 수 없습니다.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBox { Text = code, IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
        if (await MakeDialog(title, panel, "코드 복사").ShowAsync() == ContentDialogResult.Primary)
        { var data = new DataPackage(); data.SetText(code); Clipboard.SetContent(data); }
    }
    private async Task DevicesAsync()
    {
        var devices = await _client.DevicesAsync(_life.Token); var panel = new StackPanel { Spacing = 12 };
        var selection = new ComboBox { Header = "연결을 해제할 기기", ItemsSource = devices.Where(row => row.id != _client.Checkpoint!.Credentials.Device).ToArray(), DisplayMemberPath = "name", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in devices) panel.Children.Add(new TextBlock { Text = item.name + (item.id == _client.Checkpoint!.Credentials.Device ? " · 이 PC" : "") + $"\n마지막 저장 {DateTimeOffset.FromUnixTimeSeconds(item.seen).ToLocalTime():yyyy.MM.dd HH:mm}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(selection); var dialog = MakeDialog("연결 기기 (최대 10대)", panel, "선택 기기 해제"); dialog.IsPrimaryButtonEnabled = false;
        selection.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = selection.SelectedItem is SyncDevice;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && selection.SelectedItem is SyncDevice device)
        { await _client.RevokeAsync(device.id, _life.Token); _status.Text = "선택한 기기의 권한을 취소했어요. 해당 PC의 기존 기록은 유지됩니다."; }
    }
    private async Task RestoreAsync()
    {
        var backups = _client.Backups();
        if (backups.Length == 0) throw new ArgumentException("동기화로 기록을 바꾸기 전 자동 생성된 백업이 아직 없어요.");
        var choice = new ComboBox { ItemsSource = backups, DisplayMemberPath = "Label", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(new TextBlock { Text = "선택한 백업의 기록으로 이 PC의 기록을 복원합니다. 현재 기록도 먼저 백업합니다. 연결 중이면 복원한 변경은 다른 PC에도 동기화됩니다.", TextWrapping = TextWrapping.Wrap }); panel.Children.Add(choice);
        if (await MakeDialog("동기화 전 기록 복원", panel, "복원").ShowAsync() != ContentDialogResult.Primary) return;
        await _client.RestoreAsync(_settings!, ((SyncBackup)choice.SelectedItem).Path, _life.Token); Changed?.Invoke(this, EventArgs.Empty); _status.Text = "백업을 복원했어요.";
    }
    public async Task StopAsync()
    {
        _stopped = true; AppSettings.Saved -= OnSaved; CancelWatch();
        try { if (_watch is not null) await _watch; if (_operation is not null) await _operation; } catch (OperationCanceledException) { }
        _client.Dispose();
    }
}
