using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using MapleDay.Core;
using MapleDay.Services;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using Windows.UI.Notifications;

namespace MapleDay.Storage.Tests;

public sealed class WindowsNotificationTests
{
    [Fact]
    public void ReadingTabPersistsAllNoticesAndNewArrivals()
    {
        var settings = new AppSettings { ReminderNotices = [new() { Id = "old", Read = true }, new() { Id = "unread" }] };
        Assert.True(settings.MarkRemindersRead());
        Assert.False(settings.MarkRemindersRead());
        settings.ReminderNotices.Insert(0, new() { Id = "new" });
        Assert.True(settings.MarkRemindersRead());
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.All(restored.ReminderNotices, notice => Assert.True(notice.Read));
        Assert.Equal(3, restored.ReminderNotices.Count);
    }

    [Theory]
    [InlineData("reminder", "notice-123")]
    [InlineData("ticket", "문의;=& 한글")]
    public void PayloadKeepsUnicodeAndActivationDestination(string key, string value)
    {
        var xml = XDocument.Parse(PortableWindowsNotifications.Payload("메요일 <알림>", ["미완료 퀘스트 & 보스"],
            new Dictionary<string, string> { [key] = value }).GetXml());
        Assert.Equal("메요일 <알림>", xml.Descendants("text").First().Value);
        Assert.Equal("미완료 퀘스트 & 보스", xml.Descendants("text").Last().Value);
        Assert.Equal(value, PortableWindowsNotifications.ParseArguments(xml.Root!.Attribute("launch")!.Value)[key]);
    }

    [Theory]
    [InlineData(NotificationSetting.DisabledForApplication, "메요일 알림이 꺼져")]
    [InlineData(NotificationSetting.DisabledForUser, "Windows 알림이 꺼져")]
    [InlineData(NotificationSetting.DisabledByGroupPolicy, "정책")]
    public void WindowsBlockingReasonsAreReported(NotificationSetting setting, string expected) =>
        Assert.Contains(expected, PortableWindowsNotifications.StatusText(setting));

    [Fact]
    public async Task OSRegistrationAndComClickCallbackWorkWithoutStartingAppOrShowingToast()
    {
        // Register only the test host. No notification is shown and no WinUI app is launched.
        using var channel = new PortableWindowsNotifications();
        var received = new TaskCompletionSource<IReadOnlyDictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var setting = channel.Initialize(arguments => received.TrySetResult(arguments));
            Assert.True(Enum.IsDefined(setting));
            var command = $"\"{Environment.ProcessPath}\" -ToastActivated";
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID");
            var matches = classes!.GetSubKeyNames().Where(name =>
            {
                using var server = classes.OpenSubKey(name + @"\LocalServer32");
                return string.Equals(command, server?.GetValue(null) as string, StringComparison.OrdinalIgnoreCase);
            }).ToArray();
            var clsid = Guid.Parse(Assert.Single(matches));
            var iid = new Guid("53E31837-6600-4A81-9395-75CFFE746F94");
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 4, ref iid, out var instance));
            try
            {
                // Invoke the real COM vtable: .NET unwraps same-process CCWs into
                // their managed object, which cannot be cast to a second imported interface.
                var vtable = Marshal.ReadIntPtr(instance);
                var activate = Marshal.GetDelegateForFunctionPointer<ActivateCallback>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
                Marshal.ThrowExceptionForHR(activate(instance, "MapleDay.Tests", "reminder=headless-click", IntPtr.Zero, 0));
            }
            finally { Marshal.Release(instance); }
            var arguments = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("headless-click", arguments["reminder"]);
        }
        finally { channel.Dispose(); ToastNotificationManagerCompat.Uninstall(); }
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ActivateCallback(IntPtr instance, [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs, IntPtr data, uint count);
}
