using System.Runtime.InteropServices;

namespace Lumiven.Services;

public sealed class TrayService : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x51;
    private readonly SubclassProc _callback;
    private IntPtr _window, _icon;
    private Action? _show, _exit;
    private bool _installed, _ownsIcon;
    private uint _taskbarCreated;

    public TrayService() => _callback = WindowMessage;

    public void Initialize(IntPtr hwnd, Action show, Action exit)
    {
        if (_installed) return;
        _window = hwnd; _show = show; _exit = exit;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Lumiven.ico");
        if (File.Exists(iconPath))
        {
            _icon = LoadImage(IntPtr.Zero, iconPath, 1, 32, 32, 0x10);
            _ownsIcon = _icon != IntPtr.Zero;
        }
        if (_icon == IntPtr.Zero) _icon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
        if (!SetWindowSubclass(hwnd, _callback, new UIntPtr(0x51), UIntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        AddIcon();
        _installed = true;
    }

    private NotifyIconData IconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 1,
        Flags = 1 | 2 | 4, Callback = CallbackMessage, Icon = _icon,
        Tip = Localizer.T("EVEING Lumiven · 双击打开"), Info = "", InfoTitle = ""
    };

    private void AddIcon()
    {
        var data = IconData();
        if (!ShellNotifyIcon(0, ref data)) throw new System.ComponentModel.Win32Exception(Localizer.T("无法创建系统托盘图标"));
    }

    public void RefreshLanguage()
    {
        if (!_installed) return;
        var data = IconData();
        ShellNotifyIcon(1, ref data);
    }

    private IntPtr WindowMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == _taskbarCreated) AddIcon();
        if (message == CallbackMessage)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
            if (mouseMessage == 0x203) _show?.Invoke();
            if (mouseMessage is 0x205 or 0x7B) OpenMenu();
            return IntPtr.Zero;
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void OpenMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, 0, new UIntPtr(1), Localizer.T("打开 EVEING Lumiven"));
            AppendMenu(menu, 0x800, UIntPtr.Zero, null);
            AppendMenu(menu, 0, new UIntPtr(2), Localizer.T("退出并停止下载"));
            GetCursorPos(out var point);
            SetForegroundWindow(_window);
            var selected = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, _window, IntPtr.Zero);
            if (selected == 1) _show?.Invoke();
            if (selected == 2) _exit?.Invoke();
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (!_installed) return;
        var data = IconData();
        ShellNotifyIcon(2, ref data);
        RemoveWindowSubclass(_window, _callback, new UIntPtr(0x51));
        if (_ownsIcon) DestroyIcon(_icon);
        _installed = false;
    }

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr reference);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr resource);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? label);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
}
