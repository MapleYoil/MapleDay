using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
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
    private byte[]? _previewPixels;
    private bool _playing;
    private bool _drawing;
    private int _generation;
    private readonly object _renderGate = new();
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _exportCancellation;
    private Task? _exportTask;
    private bool _choosingExport;
    private bool _showResult;
    private double Duration => DurationInput is { Value: >= 1 and <= 600 } ? DurationInput.Value : IncomeReplayRenderer.Duration;
    private void SetExportControls(bool enabled)
    {
        Mp4Button.IsEnabled = GifButton.IsEnabled = ImageButton.IsEnabled = ResultButton.IsEnabled = PlayButton.IsEnabled = PauseButton.IsEnabled = enabled;
        DurationInput.IsEnabled = enabled;
    }

    public IncomeReplayView()
    {
        InitializeComponent();
        Unloaded += (_, _) => { Pause(); _generation++; _exportCancellation?.Cancel(); lock (_renderGate) { _renderer?.Dispose(); _renderer = null; } };
    }
    public void SetRecords(IncomeReplay replay, IncomeDisplay display, string scope)
    {
        if (_exportTask is { IsCompleted: false }) { Status.Text = "현재 내보내기를 마친 뒤 새 집계를 재생하세요."; return; }
        _replay = replay; _display = display; _scope = scope;
        _generation++;
        lock (_renderGate) { _renderer?.Dispose(); _renderer = new(_replay, _display, _scope, Path.Combine(AppContext.BaseDirectory, "Assets")); }
        _bitmap = null;
        SetExportControls(_replay.Clears.Count > 0);
        _showResult = false;
        Status.Text = _replay.Clears.Count > 0 ? $"{Duration:0.##}초 · MP4 1080p/60fps · GIF 720p/25fps · 결과 이미지 1080p"
            : _replay.Category == "hunting" ? "집계할 사냥 수익 기록이 없습니다." : "집계할 주간 보스·물욕템 수익 기록이 없습니다.";
        _clock.Restart(); DrawFrame(); if (_replay.Clears.Count > 0) StartPlayback(); else _clock.Stop();
    }
    public void SetDisplay(IncomeDisplay display)
    { _display = display; _generation++; lock (_renderGate) _renderer?.SetDisplay(display); if (!_playing) DrawFrame(); }
    private void StartPlayback()
    {
        if (_playing) return;
        _playing = true; CompositionTarget.Rendering += PresentFrame;
    }
    private void PresentFrame(object? sender, object args) => DrawFrame();
    public void Pause()
    {
        if (_playing) CompositionTarget.Rendering -= PresentFrame;
        _playing = false; _clock.Stop();
        if (PauseButton is not null) PauseButton.Content = "계속 재생";
    }
    private async void DrawFrame()
    {
        if (_drawing || _renderer is null) return;
        _drawing = true;
        var generation = _generation;
        var renderer = _renderer;
        var seconds = _showResult ? IncomeReplayRenderer.Duration : IncomeReplayExport.ReplayTime(_clock.Elapsed.TotalSeconds, Duration);
        // Match physical display pixels. Full-size 1080p frames remain exclusive to exports.
        var physicalWidth = Preview.ActualWidth * (XamlRoot?.RasterizationScale ?? 1);
        var width = Math.Clamp((int)(physicalWidth > 0 ? physicalWidth : 1280), 640, 1920);
        width -= width % 16;
        var height = width * 9 / 16;
        if (_previewPixels?.Length != width * height * 4) _previewPixels = new byte[width * height * 4];
        var buffer = _previewPixels;
        try
        {
            var frame = await Task.Run(() =>
            {
                lock (_renderGate)
                { return ReferenceEquals(renderer, _renderer) ? renderer.RenderPixels(seconds, width, height, buffer: buffer) : null; }
            });
            if (frame is null || generation != _generation || !ReferenceEquals(renderer, _renderer)) return;
            if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            { _bitmap = new(width, height); Preview.Source = _bitmap; }
            using var pixels = _bitmap.PixelBuffer.AsStream(); pixels.Write(frame); _bitmap.Invalidate();
            if (_clock.Elapsed.TotalSeconds >= Duration) Pause();
        }
        catch (Exception) { Pause(); Status.Text = "집계 화면을 그리지 못했어요. 다시 재생해주세요."; }
        finally { _drawing = false; if (generation != _generation && _renderer is not null && !_playing) DrawFrame(); }
    }
    private void Play_Click(object sender, RoutedEventArgs args) { _generation++; _showResult = false; _clock.Restart(); PauseButton.Content = "일시 정지"; StartPlayback(); }
    private void Result_Click(object sender, RoutedEventArgs args) { Pause(); _generation++; _showResult = true; DrawFrame(); Status.Text = "최종 집계 결과 · PNG / WEBP로 저장할 수 있습니다."; }
    private void Duration_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_renderer is null || !double.IsFinite(args.NewValue) || args.NewValue is < 1 or > 600) return;
        _generation++; _showResult = false; _clock.Restart(); StartPlayback(); PauseButton.Content = "일시 정지";
        Status.Text = $"{Duration:0.##}초 · MP4 1080p/60fps · GIF 720p/25fps · 결과 이미지 1080p";
    }
    private void Pause_Click(object sender, RoutedEventArgs args)
    { if (_playing) Pause(); else { if (_showResult || _clock.Elapsed.TotalSeconds >= Duration) { _showResult = false; _clock.Restart(); } else _clock.Start(); StartPlayback(); PauseButton.Content = "일시 정지"; } }
    private void Close_Click(object sender, RoutedEventArgs args) { Pause(); _generation++; _exportCancellation?.Cancel(); Visibility = Visibility.Collapsed; }
    private void Cancel_Click(object sender, RoutedEventArgs args) => _exportCancellation?.Cancel();
    private async void Mp4_Click(object sender, RoutedEventArgs args) => await ChooseExportAsync(false);
    private async void Gif_Click(object sender, RoutedEventArgs args) => await ChooseExportAsync(true);
    private async void Image_Click(object sender, RoutedEventArgs args) => await ChooseExportAsync(false, true);
    private async Task ChooseExportAsync(bool gif, bool image = false)
    {
        if (_choosingExport || _exportTask is { IsCompleted: false } || _replay.Clears.Count == 0) return;
        _choosingExport = true; Pause(); SetExportControls(false);
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = image ? PickerLocationId.PicturesLibrary : PickerLocationId.VideosLibrary,
                SuggestedFileName = $"메요일-{(_replay.Category == "hunting" ? "사냥" : "주간보스")}-{DateTime.Now:yyyyMMdd}" };
            if (image) { picker.FileTypeChoices.Add("PNG 결과 이미지", new List<string> { ".png" }); picker.FileTypeChoices.Add("WEBP 결과 이미지", new List<string> { ".webp" }); }
            else picker.FileTypeChoices.Add(gif ? "GIF 애니메이션" : "MP4 동영상", new List<string> { gif ? ".gif" : ".mp4" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerWindow);
            var file = await picker.PickSaveFileAsync(); if (file is null) return;
            _exportCancellation = new(); var token = _exportCancellation.Token;
            ExportProgress.Value = 0; ExportProgress.Visibility = CancelButton.Visibility = Visibility.Visible;
            Status.Text = "내보내는 중…";
            var progress = new Progress<double>(value => { ExportProgress.Value = value * 100; Status.Text = $"내보내는 중… {value:P0}"; });
            var replay = _replay; var display = _display; var scope = _scope; var duration = Duration;
            _exportTask = Task.Run(() => image
                ? IncomeReplayExport.ExportImageAsync(replay, display, scope, Path.Combine(AppContext.BaseDirectory, "Assets"), file.Path,
                    Path.GetExtension(file.Path).Equals(".webp", StringComparison.OrdinalIgnoreCase), token)
                : IncomeReplayExport.ExportAsync(replay, display, scope,
                    Path.Combine(AppContext.BaseDirectory, "Assets"), Path.Combine(AppContext.BaseDirectory, "MapleDay.Media.exe"), file.Path, gif, progress, token, duration), token);
            await _exportTask;
            Status.Text = $"저장 완료 · {file.Path}";
        }
        catch (OperationCanceledException) { Status.Text = "내보내기를 취소했습니다."; }
        catch (Exception error) { Status.Text = error is IOException ? error.Message : "내보내지 못했어요. 저장 위치와 Windows 미디어 기능을 확인하세요."; }
        finally
        {
            _exportCancellation?.Dispose(); _exportCancellation = null;
            _choosingExport = false; SetExportControls(_replay.Clears.Count > 0);
            ExportProgress.Visibility = CancelButton.Visibility = Visibility.Collapsed;
        }
    }
    public async Task StopAsync()
    {
        Pause(); _exportCancellation?.Cancel();
        if (_exportTask is { } export) try { await export; } catch { /* User-facing handler reports the outcome. */ }
    }
}
