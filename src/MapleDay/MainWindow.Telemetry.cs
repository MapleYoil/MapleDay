using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;

namespace MapleDay;

public sealed partial class MainWindow
{
    private readonly TelemetryClient _telemetry = new();
    private readonly DispatcherTimer _telemetryTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly CancellationTokenSource _telemetryLifetime = new();
    private bool _telemetryBusy, _telemetryInitialized, _telemetryPending;
    private CancellationTokenSource? _telemetryRequest;
    private void InitializeTelemetry()
    {
        UsageAnalyticsToggle.IsOn = _settings.UsageAnalyticsEnabled;
        AnonymousUsageToggle.IsOn = _settings.AnonymousUsageAnalytics;
        AnonymousUsageToggle.IsEnabled = _settings.UsageAnalyticsEnabled;
        ErrorReportingToggle.IsOn = _settings.AutomaticErrorReports;
        App.Diagnostics.SetEnabled(_settings.AutomaticErrorReports);
        _telemetryTimer.Tick += async (_, _) => await SendTelemetryAsync();
        Closed += (_, _) => StopTelemetry();
        _telemetryInitialized = true;
    }
    private void StartTelemetry() { _telemetryTimer.Start(); _ = SendTelemetryAsync(); }
    private void StopTelemetry()
    {
        _telemetryTimer.Stop(); _telemetryLifetime.Cancel();
        _telemetryRequest?.Cancel();
        App.Diagnostics.Suspend();
    }
    private void ReportDiagnostic(Exception error, string category)
    {
        if (!_closed && !_dataDeleting && _settings.AutomaticErrorReports)
            App.Diagnostics.Capture(error, category, CurrentAppVersion);
    }
    private async Task SendTelemetryAsync()
    {
        if (_closed || _dataDeleting || (!_settings.UsageAnalyticsEnabled && !_settings.AutomaticErrorReports)) return;
        if (_telemetryBusy) { _telemetryPending = true; return; }
        _telemetryPending = false;
        _telemetryBusy = true;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_telemetryLifetime.Token);
        _telemetryRequest = request;
        try
        {
            var candidates = new List<(string Name, int Level, string World)>();
            if (_settings.UsageAnalyticsEnabled)
            {
                candidates.AddRange(Characters.Select(character => (character.Name, character.Level, character.World)));
                foreach (var profile in SavedApiKeys.ToArray().Where(profile => profile.Key != _apiKey))
                {
                    try
                    {
                        var cache = await _characterCache.LoadAsync(profile.Key);
                        if (cache is not null) candidates.AddRange(cache.Characters.Select(character =>
                            (character.Basic?.Name ?? character.Summary.Name, character.Basic?.Level ?? character.Summary.Level,
                                character.Basic?.World ?? character.Summary.World)));
                    }
                    catch (Exception error) when (IsStorageError(error) || error is System.Text.Json.JsonException) { }
                }
            }
            var highest = UsageIdentity.HighestNickname(candidates.Select(character => (character.Name, character.Level)));
            var selected = candidates.Where(character => character.Name == highest).OrderByDescending(character => character.Level).FirstOrDefault();
            var usageProfile = highest is null ? null : new UsageCharacter(highest, selected.Level, selected.World);
            await _telemetry.SendAsync(highest is null ? [] : [highest], CurrentAppVersion, App.Diagnostics,
                _settings.UsageAnalyticsEnabled, request.Token, _settings.AnonymousUsageAnalytics, usageProfile);
        }
        catch (Exception) { /* Retry offline failures without interrupting the app or reporting themselves. */ }
        finally
        {
            _telemetryRequest = null; _telemetryBusy = false;
            if (_telemetryPending && !_closed && !_dataDeleting && !_telemetryLifetime.IsCancellationRequested) _ = SendTelemetryAsync();
        }
    }
    private void TelemetryToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (!_telemetryInitialized || _closed || _dataDeleting) return;
        var usage = _settings.UsageAnalyticsEnabled;
        var reports = _settings.AutomaticErrorReports;
        var anonymous = _settings.AnonymousUsageAnalytics;
        _settings.UsageAnalyticsEnabled = UsageAnalyticsToggle.IsOn;
        _settings.AutomaticErrorReports = ErrorReportingToggle.IsOn;
        _settings.AnonymousUsageAnalytics = AnonymousUsageToggle.IsOn;
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error))
        {
            _settings.UsageAnalyticsEnabled = usage; _settings.AutomaticErrorReports = reports; _settings.AnonymousUsageAnalytics = anonymous;
            _telemetryInitialized = false;
            UsageAnalyticsToggle.IsOn = usage; ErrorReportingToggle.IsOn = reports;
            AnonymousUsageToggle.IsOn = anonymous;
            _telemetryInitialized = true;
            TelemetryStatus.Text = "설정을 저장하지 못했어요. 저장 공간을 확인해주세요.";
            return;
        }
        App.Diagnostics.SetEnabled(_settings.AutomaticErrorReports);
        _telemetryRequest?.Cancel();
        AnonymousUsageToggle.IsEnabled = _settings.UsageAnalyticsEnabled;
        TelemetryStatus.Text = !_settings.UsageAnalyticsEnabled ? "사용자 집계를 껐습니다."
            : _settings.AnonymousUsageAnalytics ? "익명 집계로 설정했습니다. 닉네임 원문·레벨·서버 정보를 보내지 않습니다." : "최고 레벨 캐릭터의 닉네임·레벨·서버를 함께 보내도록 설정했습니다. API 키는 보내지 않습니다.";
        _ = SendTelemetryAsync();
    }
}
