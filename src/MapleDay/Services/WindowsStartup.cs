using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace MapleDay.Services;

public sealed class WindowsStartup
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string PackagedTaskId = "MapleDayStartup";
    public const string LaunchArgument = "--startup";
    public static bool IsStartupLaunch(bool startupTask, IEnumerable<string> arguments)
        => startupTask || arguments.Contains(LaunchArgument, StringComparer.Ordinal);
    public static bool ShouldStartInTray(bool automatic, bool toTray, bool trayAvailable)
        => automatic && toTray && trayAvailable;
    public static bool IsPackaged
    {
        get
        {
            uint length = 0;
            var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
            return result is 0 or 122; // ERROR_INSUFFICIENT_BUFFER means an identity exists.
        }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);

    public static async Task<StartupTaskState> SetPackagedAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(PackagedTaskId);
        if (!enabled) { task.Disable(); return task.State; }
        return task.State == StartupTaskState.Disabled ? await task.RequestEnableAsync() : task.State;
    }
    private readonly string _keyPath;
    public WindowsStartup(string keyPath = RunKey) => _keyPath = keyPath;

    // Register the outer native launcher so the executable directory stays clean.
    public static string? FindLauncher(string appDirectory)
    {
        var directory = new DirectoryInfo(appDirectory);
        if (!directory.Name.Equals("App", StringComparison.OrdinalIgnoreCase)) return null;
        var launcher = Path.Combine(directory.Parent!.FullName, "MapleDay.exe");
        return File.Exists(launcher) ? launcher : null;
    }

    public void Enable(string launcher)
    {
        var path = Path.GetFullPath(launcher);
        if (!File.Exists(path)) throw new FileNotFoundException("실행 파일을 찾을 수 없습니다.", path);
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        var command = $"\"{path}\" {LaunchArgument}";
        if (!string.Equals(key.GetValue("MapleDay") as string, command, StringComparison.Ordinal))
            key.SetValue("MapleDay", command, RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true);
        key?.DeleteValue("MapleDay", throwOnMissingValue: false);
    }
}
