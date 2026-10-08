using System.Runtime.InteropServices;

namespace MapleDay.Services;

/// <summary>Keep the badge pending until Windows creates the taskbar button, including after Explorer restarts.</summary>
internal sealed class TaskbarUpdateBadge : IDisposable
{
    private readonly nint _window;
    private readonly SubclassProcedure _procedure;
    private readonly uint _createdMessage = RegisterWindowMessage("TaskbarButtonCreated");
    private readonly nint _icon = UpdateBadgeIcon.Create(16);
    private ITaskbarList3? _taskbar;
    private bool _visible, _ready, _disposed;
    private readonly bool _subclassed;

    public TaskbarUpdateBadge(nint window)
    {
        _window = window;
        _procedure = ProcessMessage;
        _subclassed = SetWindowSubclass(window, _procedure, 0x4D445550, 0);
    }

    public void SetVisible(bool visible)
    {
        if (_disposed) return;
        _visible = visible;
        Apply();
    }

    private nint ProcessMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        if (message == _createdMessage)
        {
            _ready = true;
            ReleaseTaskbar();
            Apply();
        }
        if (message == 0x82) { RemoveWindowSubclass(window, _procedure, id); _ready = false; }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void Apply()
    {
        if (!_ready || _disposed) return;
        try
        {
            if (_taskbar is null)
            {
                _taskbar = (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"))!)!;
                _taskbar.HrInit();
            }
            _taskbar.SetOverlayIcon(_window, _visible ? _icon : 0, _visible ? "새 버전 준비됨 · 설정에서 업데이트" : "");
        }
        catch (Exception error) when (error is COMException or InvalidCastException)
        { ReleaseTaskbar(); }
    }

    private void ReleaseTaskbar()
    {
        if (_taskbar is not null) Marshal.ReleaseComObject(_taskbar);
        _taskbar = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        SetVisible(false);
        _disposed = true;
        if (_subclassed) RemoveWindowSubclass(_window, _procedure, 0x4D445550);
        ReleaseTaskbar();
        UpdateBadgeIcon.Destroy(_icon);
        GC.KeepAlive(_procedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint reference);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);

    // Flatten inherited methods to preserve ITaskbarList3's native vtable order.
    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(nint window);
        void DeleteTab(nint window);
        void ActivateTab(nint window);
        void SetActiveAlt(nint window);
        void MarkFullscreenWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(nint window, ulong completed, ulong total);
        void SetProgressState(nint window, uint state);
        void RegisterTab(nint tab, nint parent);
        void UnregisterTab(nint tab);
        void SetTabOrder(nint tab, nint before);
        void SetTabActive(nint tab, nint parent, uint reserved);
        void ThumbBarAddButtons(nint window, uint count, nint buttons);
        void ThumbBarUpdateButtons(nint window, uint count, nint buttons);
        void ThumbBarSetImageList(nint window, nint images);
        void SetOverlayIcon(nint window, nint icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
        void SetThumbnailTooltip(nint window, [MarshalAs(UnmanagedType.LPWStr)] string tip);
        void SetThumbnailClip(nint window, nint rectangle);
    }
}
