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
            var candidates = new List<(string Name, int Level)>();
            if (_settings.UsageAnalyticsEnabled)
            {
                candidates.AddRange(Characters.Select(character => (character.Name, character.Level)));
                foreach (var profile in SavedApiKeys.ToArray().Where(profile => profile.Key != _apiKey))
                {
                    try
                    {
                        var cache = await _characterCache.LoadAsync(profile.Key);
                        if (cache is not null) candidates.AddRange(cache.Characters.Select(character =>
                            (character.Basic?.Name ?? character.Summary.Name, character.Basic?.Level ?? character.Summary.Level)));
                    }
                    catch (Exception error) when (IsStorageError(error) || error is System.Text.Json.JsonException) { }
                }
            }
            var highest = UsageIdentity.HighestNickname(candidates);
            await _telemetry.SendAsync(highest is null ? [] : [highest], CurrentAppVersion, App.Diagnostics,
                _settings.UsageAnalyticsEnabled, request.Token);
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
        _settings.UsageAnalyticsEnabled = UsageAnalyticsToggle.IsOn;
        _settings.AutomaticErrorReports = ErrorReportingToggle.IsOn;
        try { _settings.Save(); }
        catch (Exception error) when (IsStorageError(error))
        {
            _settings.UsageAnalyticsEnabled = usage; _settings.AutomaticErrorReports = reports;
            _telemetryInitialized = false;
            UsageAnalyticsToggle.IsOn = usage; ErrorReportingToggle.IsOn = reports;
            _telemetryInitialized = true;
            TelemetryStatus.Text = "설정을 저장하지 못했어요. 저장 공간을 확인해주세요.";
            return;
        }
        App.Diagnostics.SetEnabled(_settings.AutomaticErrorReports);
        _telemetryRequest?.Cancel();
        TelemetryStatus.Text = "설정을 저장했습니다. 닉네임 원문과 API 키는 집계·오류 서버로 전송하지 않습니다.";
        _ = SendTelemetryAsync();
    }
}
