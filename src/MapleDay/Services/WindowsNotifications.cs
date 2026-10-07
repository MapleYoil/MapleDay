using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.AppNotifications;
using Windows.UI.Notifications;

namespace MapleDay.Services;

public sealed class WindowsNotifications : IDisposable
{
    private readonly PortableWindowsNotifications _portable = new();
    private bool _packagedRegistered;
    private Action<IReadOnlyDictionary<string, string>>? _activated;
    public bool Ready { get; private set; }
    public string Status { get; private set; } = "Windows 알림을 연결하는 중이에요.";

    public void Initialize(Action<IReadOnlyDictionary<string, string>> activated)
    {
        _activated = activated;
        try
        {
            NotificationSetting setting;
            if (WindowsStartup.IsPackaged)
            {
                if (!AppNotificationManager.IsSupported())
                {
                    Ready = false;
                    Status = "현재 실행 환경에서는 Windows 알림을 사용할 수 없어요. 관리자 권한으로 실행 중이라면 일반 권한으로 다시 실행해주세요.";
                    RecordStatus();
                    return;
                }
                if (!_packagedRegistered)
                {
                    AppNotificationManager.Default.NotificationInvoked += PackagedActivated;
                    try { AppNotificationManager.Default.Register(); _packagedRegistered = true; }
                    catch { AppNotificationManager.Default.NotificationInvoked -= PackagedActivated; throw; }
                }
                setting = (NotificationSetting)(int)AppNotificationManager.Default.Setting;
            }
            else setting = _portable.Initialize(activated);
            Ready = setting == NotificationSetting.Enabled;
            Status = PortableWindowsNotifications.StatusText(setting);
            RecordStatus();
        }
        catch (Exception error) when (IsNotificationError(error)) { Failed(error); }
    }

    public bool Show(string title, IEnumerable<string> lines, IReadOnlyDictionary<string, string> arguments)
    {
        try
        {
            if (!Ready) return false;
            var setting = WindowsStartup.IsPackaged ? (NotificationSetting)(int)AppNotificationManager.Default.Setting : _portable.Setting;
            if (setting != NotificationSetting.Enabled)
            {
                Ready = false; Status = PortableWindowsNotifications.StatusText(setting); RecordStatus(); return false;
            }
            var payload = PortableWindowsNotifications.Payload(title, lines, arguments);
            if (_packagedRegistered) AppNotificationManager.Default.Show(new AppNotification(payload.GetXml()));
            else _portable.Show(payload);
            return true;
        }
        catch (Exception error) when (IsNotificationError(error)) { Failed(error); return false; }
    }

    private void PackagedActivated(AppNotificationManager sender, AppNotificationActivatedEventArgs args) =>
        _activated?.Invoke(args.Arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    private static bool IsNotificationError(Exception error) => error is COMException or InvalidOperationException
        or UnauthorizedAccessException or IOException or ArgumentException or TypeInitializationException;
    private void Failed(Exception error)
    {
        Ready = false;
        var cause = error.GetBaseException();
        Status = $"Windows 알림을 연결하지 못했어요 (0x{cause.HResult:X8}). 알림 기록은 이 탭에서 확인할 수 있습니다.";
        RecordStatus(cause.HResult);
    }
    private void RecordStatus(int? error = null)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleDay");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "notification-status.json"), JsonSerializer.Serialize(new
            { UpdatedAt = DateTimeOffset.UtcNow, Backend = WindowsStartup.IsPackaged ? "WindowsAppSDK" : "WindowsToastCompat", Ready, Status, HResult = error }));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        _portable.Dispose();
        if (_packagedRegistered)
        {
            AppNotificationManager.Default.NotificationInvoked -= PackagedActivated;
            try { AppNotificationManager.Default.Unregister(); }
            catch (Exception error) when (IsNotificationError(error)) { }
            _packagedRegistered = false;
        }
        Ready = false; _activated = null;
    }
}
