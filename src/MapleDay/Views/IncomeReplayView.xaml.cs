using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace MapleDay.Views;

public sealed partial class IncomeReplayView : UserControl
{
    public nint OwnerWindow { get; set; }
    private IncomeReplay _replay = new(Array.Empty<ReplayClear>());
    private IncomeDisplay _display = new();
    private string _scope = "전체 캐릭터";
    private IncomeReplayRenderer? _renderer;
    private WriteableBitmap? _bitmap;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _exportCancellation;
    private Task? _exportTask;
    private bool _choosingExport;

    public IncomeReplayView()
    {
        InitializeComponent(); _timer.Tick += (_, _) => DrawFrame();
        Unloaded += (_, _) => { Pause(); _exportCancellation?.Cancel(); _renderer?.Dispose(); _renderer = null; };
    }
    public void SetRecords(IEnumerable<BossIncomeRecord> records, IncomeDisplay display, string scope)
    {
        if (_exportTask is { IsCompleted: false }) { Status.Text = "현재 내보내기를 마친 뒤 새 집계를 재생하세요."; return; }
        _replay = new(records); _display = display; _scope = scope;
        _renderer?.Dispose(); _renderer = new(_replay, _display, _scope, Path.Combine(AppContext.BaseDirectory, "Assets"));
        _bitmap = new(IncomeReplayRenderer.PreviewWidth, IncomeReplayRenderer.PreviewHeight); Preview.Source = _bitmap;
        Mp4Button.IsEnabled = GifButton.IsEnabled = PlayButton.IsEnabled = PauseButton.IsEnabled = _replay.Clears.Count > 0;
        Status.Text = _replay.Clears.Count > 0 ? $"{IncomeReplayRenderer.Duration:0}초 · 미리보기 1080p · MP4 1080p/60fps · GIF 720p/25fps"
            : "집계할 확정 주간 보스 수익 기록이 없습니다.";
        _clock.Restart(); DrawFrame(); if (_replay.Clears.Count > 0) _timer.Start(); else _clock.Stop();
    }
    public void SetDisplay(IncomeDisplay display) { _display = display; _renderer?.SetDisplay(display); if (!_timer.IsEnabled) DrawFrame(); }
    public void Pause() { _timer.Stop(); _clock.Stop(); if (PauseButton is not null) PauseButton.Content = "계속 재생"; }
    private void DrawFrame()
    {
        if (_renderer is null || _bitmap is null) return;
        using var frame = _renderer.Render(Math.Min(IncomeReplayRenderer.Duration, _clock.Elapsed.TotalSeconds));
        using var pixels = _bitmap.PixelBuffer.AsStream(); pixels.Write(IncomeReplayRenderer.Pixels(frame)); _bitmap.Invalidate();
        if (_clock.Elapsed.TotalSeconds >= IncomeReplayRenderer.Duration) Pause();
    }
    private void Play_Click(object sender, RoutedEventArgs args) { _clock.Restart(); PauseButton.Content = "일시 정지"; _timer.Start(); }
    private void Pause_Click(object sender, RoutedEventArgs args)
    { if (_timer.IsEnabled) Pause(); else { _clock.Start(); _timer.Start(); PauseButton.Content = "일시 정지"; } }
    private void Close_Click(object sender, RoutedEventArgs args) { Pause(); _exportCancellation?.Cancel(); Visibility = Visibility.Collapsed; }
    private void Cancel_Click(object sender, RoutedEventArgs args) => _exportCancellation?.Cancel();
    private async void Mp4_Click(object sender, RoutedEventArgs args) => await ChooseExportAsync(false);
    private async void Gif_Click(object sender, RoutedEventArgs args) => await ChooseExportAsync(true);
    private async Task ChooseExportAsync(bool gif)
    {
        if (_choosingExport || _exportTask is { IsCompleted: false } || _replay.Clears.Count == 0) return;
        _choosingExport = true; Pause(); Mp4Button.IsEnabled = GifButton.IsEnabled = false;
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.VideosLibrary,
                SuggestedFileName = $"메요일-주간보스-{DateTime.Now:yyyyMMdd}" };
            picker.FileTypeChoices.Add(gif ? "GIF 애니메이션" : "MP4 동영상", new List<string> { gif ? ".gif" : ".mp4" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerWindow);
            var file = await picker.PickSaveFileAsync(); if (file is null) return;
            _exportCancellation = new(); var token = _exportCancellation.Token;
            ExportProgress.Value = 0; ExportProgress.Visibility = CancelButton.Visibility = Visibility.Visible;
            Status.Text = "내보내는 중…";
            var progress = new Progress<double>(value => { ExportProgress.Value = value * 100; Status.Text = $"내보내는 중… {value:P0}"; });
            var replay = _replay; var display = _display; var scope = _scope;
            _exportTask = Task.Run(() => IncomeReplayExport.ExportAsync(replay, display, scope,
                Path.Combine(AppContext.BaseDirectory, "Assets"), Path.Combine(AppContext.BaseDirectory, "MapleDay.Media.exe"), file.Path, gif, progress, token), token);
            await _exportTask;
            Status.Text = $"저장 완료 · {file.Path}";
        }
        catch (OperationCanceledException) { Status.Text = "내보내기를 취소했습니다."; }
        catch (Exception error) { Status.Text = error is IOException ? error.Message : "내보내지 못했어요. 저장 위치와 Windows 미디어 기능을 확인하세요."; }
        finally
        {
            _exportCancellation?.Dispose(); _exportCancellation = null;
            _choosingExport = false; Mp4Button.IsEnabled = GifButton.IsEnabled = _replay.Clears.Count > 0;
            ExportProgress.Visibility = CancelButton.Visibility = Visibility.Collapsed;
        }
    }
    public async Task StopAsync()
    {
        Pause(); _exportCancellation?.Cancel();
        if (_exportTask is { } export) try { await export; } catch { /* User-facing handler reports the outcome. */ }
    }
}
