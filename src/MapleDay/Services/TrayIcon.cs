using System.Runtime.InteropServices;

namespace MapleDay.Services;

/// <summary>Native notification-area icon; menu and callbacks run on the WinUI thread.</summary>
public sealed class TrayIcon : IDisposable
{
    private const uint TrayMessage = 0x8001;
    private readonly WindowProcedure _procedure;
    private readonly IntPtr _window, _icon;
    private readonly Action _show, _exit;
    private readonly string _className = "MapleDayTray." + Guid.NewGuid().ToString("N");
    private NotifyIconData _data;
    private bool _disposed;
    public bool Available { get; }
    private readonly uint _taskbarCreated;

    public TrayIcon(Action show, Action exit)
    {
        _show = show; _exit = exit;
        _procedure = ProcessMessage;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        var windowClass = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Procedure = _procedure, ClassName = _className, Instance = GetModuleHandle(null) };
        if (RegisterClassEx(ref windowClass) == 0) return;
        // Hidden top-level window receives TaskbarCreated after Explorer restarts.
        _window = CreateWindowEx(0, _className, "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, windowClass.Instance, IntPtr.Zero);
        _icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "mapleday.ico"), 1, 32, 32, 0x10);
        _data = new NotifyIconData { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 1, Flags = 7, Callback = TrayMessage, Icon = _icon, Tip = "메요일 · MapleDay", Info = "", InfoTitle = "" };
        Available = _window != IntPtr.Zero && _icon != IntPtr.Zero && ShellNotifyIcon(0, ref _data);
    }

    private IntPtr ProcessMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreated) ShellNotifyIcon(0, ref _data);
        if (message == TrayMessage)
        {
            var mouseMessage = (uint)lParam.ToInt64();
            if (mouseMessage is 0x202 or 0x203) _show();
            else if (mouseMessage == 0x205)
            {
                var menu = CreatePopupMenu();
                try
                {
                    AppendMenu(menu, 0, 1, "메요일 열기");
                    AppendMenu(menu, 0, 2, "앱 종료");
                    GetCursorPos(out var point);
                    SetForegroundWindow(window);
                    var selected = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, window, IntPtr.Zero);
                    if (selected == 1) _show();
                    if (selected == 2) _exit();
                    PostMessage(window, 0, IntPtr.Zero, IntPtr.Zero);
                }
                finally { DestroyMenu(menu); }
            }
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Available) ShellNotifyIcon(2, ref _data);
        if (_window != IntPtr.Zero) DestroyWindow(_window);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        UnregisterClass(_className, GetModuleHandle(null));
        GC.KeepAlive(_procedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    {
        public uint Size, Style; public WindowProcedure Procedure; public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}
