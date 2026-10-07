using Microsoft.Toolkit.Uwp.Notifications;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace MapleDay.Services;

// Uses the OS toast APIs directly. Self-contained Windows App SDK 2.5.1's
// unpackaged Register() fails in WindowsAppRuntime_IsSelfContained (issue #6774).
public sealed class PortableWindowsNotifications : IDisposable
{
    private ToastNotifierCompat? _notifier;
    private Action<IReadOnlyDictionary<string, string>>? _activated;
    private bool _subscribed;

    public NotificationSetting Initialize(Action<IReadOnlyDictionary<string, string>> activated)
    {
        _activated = activated;
        if (!_subscribed)
        {
            ToastNotificationManagerCompat.OnActivated += OnActivated;
            _subscribed = true;
        }
        _notifier = ToastNotificationManagerCompat.CreateToastNotifier();
        return _notifier.Setting;
    }

    public NotificationSetting Setting => _notifier?.Setting ?? throw new InvalidOperationException("알림이 연결되지 않았습니다.");
    public void Show(XmlDocument payload) => (_notifier ?? throw new InvalidOperationException("알림이 연결되지 않았습니다."))
        .Show(new ToastNotification(payload));

    public static XmlDocument Payload(string title, IEnumerable<string> lines, IReadOnlyDictionary<string, string> arguments)
    {
        var builder = new ToastContentBuilder().AddText(title);
        foreach (var line in lines) builder.AddText(line);
        foreach (var argument in arguments) builder.AddArgument(argument.Key, argument.Value);
        return builder.GetToastContent().GetXml();
    }

    public static IReadOnlyDictionary<string, string> ParseArguments(string arguments) =>
        ToastArguments.Parse(arguments).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    private void OnActivated(ToastNotificationActivatedEventArgsCompat args) => _activated?.Invoke(ParseArguments(args.Argument));

    public static string StatusText(NotificationSetting setting) => setting switch
    {
        NotificationSetting.Enabled => "Windows 알림이 연결되어 있어요.",
        NotificationSetting.DisabledForApplication => "Windows 설정에서 메요일 알림이 꺼져 있어요. 시스템 → 알림에서 켜주세요.",
        NotificationSetting.DisabledForUser => "Windows 알림이 꺼져 있어요. 설정 → 시스템 → 알림에서 켜주세요.",
        NotificationSetting.DisabledByGroupPolicy => "Windows 정책에서 알림을 차단하고 있어요.",
        _ => "이 환경에서는 Windows 알림을 사용할 수 없어요."
    };

    public void Dispose()
    {
        if (_subscribed) ToastNotificationManagerCompat.OnActivated -= OnActivated;
        _subscribed = false;
        _activated = null;
        // Keep COM launch registration so notifications can reopen the app.
        // Uninstall() is only appropriate when uninstalling, never at exit.
    }
}
