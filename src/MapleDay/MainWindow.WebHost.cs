using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography.X509Certificates;
using MapleDay.Core;
using MapleDay.Models;
using MapleDay.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace MapleDay;

public sealed partial class MainWindow
{
    private WebHostServer? _webHost;
    private X509Certificate2? _webCertificate;
    private CancellationTokenSource? _webHostRequest;
    private Task? _webStartTask, _webStopTask;
    private readonly DispatcherTimer _webSnapshotTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, string> _webCharacterIds = [];
    private readonly Dictionary<WriteableBitmap, byte[]> _webImageCache = [];

    private void InitializeWebHost()
    {
        WebPortInput.Value = _settings.WebPort is >= 1024 and <= 65535 ? _settings.WebPort : 17831;
        WebPublicHostInput.Text = _settings.WebPublicHost;
        WebCertificatePathInput.Text = _settings.WebCertificatePath;
        WebHttpsToggle.IsOn = _settings.WebHttps;
        WebCertificateInputs.Visibility = _settings.WebHttps ? Visibility.Visible : Visibility.Collapsed;
        UpdateWebPasswordHint();
        _webSnapshotTimer.Tick += (_, _) => PublishWebSnapshot();
        Closed += async (_, _) => await StopWebHostAsync();
    }

    private void UpdateWebPasswordHint()
    {
        var saved = _settings.WebPasswordHash.Length > 0;
        WebPasswordInput.PlaceholderText = saved ? "비워두면 기존 비밀번호 사용" : "비밀번호 설정";
        WebPasswordHint.Text = saved ? "비밀번호가 저장되어 있습니다. 변경하려면 새 비밀번호를 입력하고 호스트를 시작하세요."
            : "8자 이상의 비밀번호를 설정하세요. 비밀번호 원문은 저장하지 않습니다.";
    }
    private void WebHttpsToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (WebCertificateInputs is not null)
            WebCertificateInputs.Visibility = WebHttpsToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SetWebInputsEnabled(bool enabled)
    {
        WebPortInput.IsEnabled = WebPasswordInput.IsEnabled = WebPublicHostInput.IsEnabled = WebHttpsToggle.IsEnabled
            = WebCertificatePathInput.IsEnabled = WebCertificatePasswordInput.IsEnabled = enabled;
    }

    private async void WebStartButton_Click(object sender, RoutedEventArgs args)
    {
        if (_closed || _dataDeleting || _webStartTask is { IsCompleted: false } || _webHost is not null) return;
        _webStartTask = StartWebHostAsync();
        await _webStartTask;
    }
    private async Task StartWebHostAsync()
    {
        WebStartButton.IsEnabled = false;
        SetWebInputsEnabled(false);
        WebHostStatus.Text = "웹 호스트를 시작하는 중…";
        _webHostRequest = new();
        var cancellation = _webHostRequest.Token;
        try
        {
            if (double.IsNaN(WebPortInput.Value) || WebPortInput.Value != Math.Truncate(WebPortInput.Value)
                || WebPortInput.Value is < 1024 or > 65535)
                throw new InvalidOperationException("포트는 1024 ~ 65535 사이의 정수로 입력하세요.");
            var port = (int)WebPortInput.Value;
            var publicHost = WebPublicHostInput.Text.Trim();
            var scheme = WebHttpsToggle.IsOn ? "https" : "http";
            if (publicHost.Length > 0 && (!Uri.TryCreate($"{scheme}://{publicHost}:{port}/", UriKind.Absolute, out var address)
                || address.AbsolutePath != "/" || address.UserInfo.Length > 0 || address.Query.Length > 0 || address.Fragment.Length > 0))
                throw new InvalidOperationException("외부 주소에는 IP 또는 DDNS 호스트 이름만 입력하세요. 포트는 위에서 설정합니다.");
            var input = WebPasswordInput.Password;
            var password = input.Length > 0 ? await Task.Run(() => WebPassword.Create(input), cancellation)
                : new WebPassword(_settings.WebPasswordSalt, _settings.WebPasswordHash);
            if (!password.Valid) throw new InvalidOperationException("접속 비밀번호를 먼저 설정하세요.");
            cancellation.ThrowIfCancellationRequested();
            if (WebHttpsToggle.IsOn)
            {
                if (string.IsNullOrWhiteSpace(WebCertificatePathInput.Text))
                    throw new InvalidOperationException("HTTPS 인증서 파일의 경로를 입력하세요.");
                _webCertificate = X509CertificateLoader.LoadPkcs12FromFile(WebCertificatePathInput.Text.Trim(),
                    WebCertificatePasswordInput.Password, X509KeyStorageFlags.DefaultKeySet);
                if (!_webCertificate.HasPrivateKey || _webCertificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                    throw new InvalidOperationException("개인키를 포함한 유효한 인증서를 선택하세요.");
            }
            _settings.WebPort = port;
            _settings.WebPasswordSalt = password.Salt;
            _settings.WebPasswordHash = password.Hash;
            _settings.WebHttps = WebHttpsToggle.IsOn;
            _settings.WebCertificatePath = WebCertificatePathInput.Text.Trim();
            _settings.WebPublicHost = publicHost;
            _settings.Save();
            WebPasswordInput.Password = "";
            WebCertificatePasswordInput.Password = "";
            UpdateWebPasswordHint();
            _webHost = new();
            await _webHost.StartAsync(port, password, Path.Combine(AppContext.BaseDirectory, "Assets"), _webCertificate,
                cancellationToken: cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (_closed || _dataDeleting) return;
            PublishWebSnapshot();
            _webSnapshotTimer.Start();
            var urls = new List<string> { $"이 PC: {_webHost.LocalUrl}" };
            foreach (var ip in NetworkInterface.GetAllNetworkInterfaces().Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses).Select(item => item.Address)
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)).Distinct())
                urls.Add($"내부 네트워크: {scheme}://{ip}:{port}");
            if (publicHost.Length > 0) urls.Add($"외부 네트워크: {scheme}://{publicHost}:{port}");
            WebHostAddresses.Text = string.Join(Environment.NewLine, urls);
            WebHostAddresses.Visibility = WebHostLinks.Visibility = Visibility.Visible;
            WebHostStatus.Text = "웹 호스트 실행 중 · PC 상태를 5초마다 준비하고 웹 화면은 15초마다 확인합니다.";
            WebStopButton.IsEnabled = true;
        }
        catch (OperationCanceledException) { /* Closing/deleting waits for this operation and disposes the server. */ }
        catch (Exception error)
        {
            if (_webHost is { } server) await server.DisposeAsync();
            _webHost = null;
            _webCertificate?.Dispose(); _webCertificate = null;
            if (!_closed && !_dataDeleting)
            {
                WebHostStatus.Text = error is ArgumentException or InvalidOperationException ? error.Message
                    : error is IOException or SocketException ? "호스트를 시작하지 못했어요. 포트 사용 상태와 파일 접근 권한을 확인하세요."
                    : "호스트를 시작하지 못했어요. 인증서 파일·비밀번호와 네트워크 설정을 확인하세요.";
            }
        }
        finally
        {
            if (!_closed && !_dataDeleting)
            { WebStartButton.IsEnabled = _webHost is null; SetWebInputsEnabled(_webHost is null); }
        }
    }
    private async void WebStopButton_Click(object sender, RoutedEventArgs args) => await StopWebHostAsync();
    private Task StopWebHostAsync()
    {
        if (_webStopTask is { IsCompleted: false }) return _webStopTask;
        return _webStopTask = StopWebHostCoreAsync();
    }
    private async Task StopWebHostCoreAsync()
    {
        _webSnapshotTimer.Stop();
        _webHostRequest?.Cancel();
        if (_webStartTask is { } pending) await pending;
        try { if (_webHost is { } server) await server.DisposeAsync(); }
        finally
        {
            _webHost = null;
            _webCertificate?.Dispose(); _webCertificate = null;
            _webHostRequest?.Dispose(); _webHostRequest = null;
            _webCharacterIds.Clear(); _webImageCache.Clear();
            if (!_closed && !_dataDeleting)
            {
                WebHostStatus.Text = "웹 호스트 꺼짐 · 기존 웹 로그인도 모두 종료했습니다.";
                WebHostAddresses.Visibility = WebHostLinks.Visibility = Visibility.Collapsed;
                WebStartButton.IsEnabled = true; SetWebInputsEnabled(true);
                WebStopButton.IsEnabled = false;
            }
        }
    }
    private async void WebOpenButton_Click(object sender, RoutedEventArgs args)
    {
        if (_webHost?.Running == true) await Launcher.LaunchUriAsync(new Uri(_webHost.LocalUrl));
    }
    private void WebCopyButton_Click(object sender, RoutedEventArgs args)
    {
        var content = new DataPackage(); content.SetText(WebHostAddresses.Text); Clipboard.SetContent(content);
    }

    private void PublishWebSnapshot()
    {
        if (_webHost?.Running != true || _closed || _dataDeleting) return;
        var images = new Dictionary<string, byte[]>();
        var bitmaps = new HashSet<WriteableBitmap>();
        var characters = new List<WebCharacter>();
        foreach (var group in SchedulerAvatars)
        {
            if (!_webCharacterIds.TryGetValue(group.Ocid, out var id))
                _webCharacterIds[group.Ocid] = id = Guid.NewGuid().ToString("N");
            var character = group.Character;
            string? image = null;
            if (character.ImageSource is { } bitmap)
            {
                bitmaps.Add(bitmap);
                if (!_webImageCache.TryGetValue(bitmap, out var bytes))
                    _webImageCache[bitmap] = bytes = EncodeWebImage(bitmap);
                images[id] = bytes; image = $"/images/{id}.png";
            }
            var level = LevelCharacters.FirstOrDefault(item => item.Ocid == group.Ocid);
            var points = level?.Trend?.Days.Where(day => day.Value is not null && day.Date >= level.Trend.EndDate.AddDays(-6)
                && day.Date <= level.Trend.EndDate).OrderBy(day => day.Date).Select(day => new WebExpPoint(
                day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), day.Value!.Level,
                day.Value.Exp.ToString(CultureInfo.InvariantCulture), (double)day.Value.Percent)).ToArray() ?? [];
            var income = group.Income;
            var today = SchedulerBossHistory.KoreanToday(DateTimeOffset.UtcNow);
            var loot = StoredLoot.Where(record => record.Ocid == group.Ocid && record.Date <= today).ToArray();
            var forecast = ForecastFor(group, today);
            characters.Add(new(id, character.Name, character.World, character.Class, character.Level, level?.LevelText ?? character.LevelText,
                group.Status, group.UpdatedAt, group.WeeklyCleared, group.Daily.Select(WebTile).ToArray(), group.Weekly.Select(WebTile).ToArray(),
                group.Bosses.Concat(group.UnregisteredBosses).Select(WebTile).ToArray(),
                new((income?.Weekly ?? 0) + BossLoot.Sum(loot, SchedulerBossHistory.Start(BossCycle.Weekly, today), today),
                    (income?.Monthly ?? 0) + BossLoot.Sum(loot, new(today.Year, today.Month, 1), today),
                    (income?.Total ?? 0) + BossLoot.Sum(loot, DateOnly.MinValue, today), income?.Records.Where(record => !record.DateEstimated)
                    .Select(record => new WebClear(record.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), record.Name, record.Difficulty,
                        record.PartySize, record.Meso, record.Included)).ToArray() ?? [], forecast?.Meso ?? 0, forecast?.Unpriced ?? 0,
                    loot.Select(record => new WebLoot(record.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), record.Boss
                        + (!string.IsNullOrEmpty(record.Difficulty) ? " · " + BossLootCatalog.DifficultyLabel(record.Difficulty) : ""),
                        record.ItemName, BossLootCatalog.Items.FirstOrDefault(item => item.Id == record.ItemId)?.Icon,
                        record.PartySize, BossLoot.Distribution(record), record.Received)).ToArray()),
                new(level?.TodayAmount ?? "현재 데이터 없음", level?.Average ?? "데이터 부족", level?.LevelUp ?? "7일 기록이 필요해요",
                    level?.Percent ?? 0, points), image));
        }
        foreach (var removed in _webCharacterIds.Keys.Where(key => !SchedulerAvatars.Any(group => group.Ocid == key)).ToArray())
            _webCharacterIds.Remove(removed);
        foreach (var removed in _webImageCache.Keys.Where(bitmap => !bitmaps.Contains(bitmap)).ToArray()) _webImageCache.Remove(removed);
        _webHost.Publish(new(typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "", DateTimeOffset.UtcNow, characters,
            CurrentIncomeDisplay.ValidMode, CurrentIncomeDisplay.Rate), images);
    }
    private static WebEntry WebTile(SchedulerTile tile) => new(tile.Name, tile.Difficulty, tile.Cycle ?? "",
        tile.ProgressVisibility == Visibility.Visible && tile.Difficulty.Length == 0 ? tile.Progress : "",
        tile.ProgressUnit, tile.IsComplete, tile.IsLevelBlocked,
        tile.IsLevelBlocked ? $"Lv. {tile.RequiredLevel}" : tile.IsQuest
            ? tile.RowActionImage.Contains("btStart", StringComparison.Ordinal) ? "시작 전"
            : tile.RowActionImage.Contains("disabled", StringComparison.Ordinal) ? "진행 중" : "완료 가능" : "미완료",
        tile.Difficulty.Length > 0 ? tile.PartySize : null, SchedulerIconAssets.BossFile(tile.Name), SchedulerIconAssets.DifficultyFile(tile.Difficulty));
    private static byte[] EncodeWebImage(WriteableBitmap bitmap)
    {
        using var drawing = new System.Drawing.Bitmap(bitmap.PixelWidth, bitmap.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var pixels = drawing.LockBits(new(0, 0, drawing.Width, drawing.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            using var source = bitmap.PixelBuffer.AsStream();
            var row = new byte[bitmap.PixelWidth * 4];
            for (var y = 0; y < bitmap.PixelHeight; y++)
            { source.ReadExactly(row); Marshal.Copy(row, 0, IntPtr.Add(pixels.Scan0, y * pixels.Stride), row.Length); }
        }
        finally { drawing.UnlockBits(pixels); }
        using var output = new MemoryStream(); drawing.Save(output, System.Drawing.Imaging.ImageFormat.Png); return output.ToArray();
    }
}
