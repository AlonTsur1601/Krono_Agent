using System.Runtime.InteropServices;

namespace Krono_Agent;

internal sealed class TrayIcon : IDisposable
{
    const uint NotifyMessage = 0x8001;
    const uint AddIcon = 0, DeleteIcon = 2, SetVersion = 4;
    const uint ContextMenu = 0x007B, LeftButtonUp = 0x0202, KeySelect = 0x0401;
    const uint OpenCommand = 1, QuitCommand = 2;
    readonly nint window, icon;
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    readonly SubclassProc windowProc;
    readonly Action open, quit;
    readonly bool hooked;
    bool disposed;

    public bool IsAvailable { get; private set; }

    public TrayIcon(nint window, string iconPath, Action open, Action quit)
    {
        this.window = window;
        this.open = open;
        this.quit = quit;
        windowProc = WindowProc;
        icon = LoadImage(0, iconPath, 1, GetSystemMetrics(49), GetSystemMetrics(50), 0x0010);
        hooked = icon != 0 && SetWindowSubclass(window, windowProc, 1, 0);
        if (hooked) RegisterIcon();
    }

    void RegisterIcon()
    {
        var data = IconData();
        IsAvailable = ShellNotifyIcon(AddIcon, ref data);
        if (!IsAvailable) return;
        data.Version = 4;
        if (ShellNotifyIcon(SetVersion, ref data)) return;
        ShellNotifyIcon(DeleteIcon, ref data);
        IsAvailable = false;
    }

    NotifyIconData IconData() => new() {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = 1,
        Flags = 0x0087, CallbackMessage = NotifyMessage, Icon = icon,
        Tip = "Krono Agent", Info = "", InfoTitle = ""
    };

    nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        if (message == taskbarCreated && taskbarCreated != 0) {
            RegisterIcon();
            if (!IsAvailable && !IsWindowVisible(window)) open();
        }
        if (message == NotifyMessage) {
            switch ((uint)lParam & 0xffff) {
                case LeftButtonUp:
                case KeySelect:
                    open();
                    return 0;
                case ContextMenu:
                    ShowMenu(unchecked((short)wParam), unchecked((short)(wParam >> 16)));
                    return 0;
            }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    void ShowMenu(int x, int y)
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        uint command = 0;
        // Keep the popup in the foreground without activating the main app window.
        var menuWindow = CreateWindowEx(0x80, "STATIC", "", 0x80000000, -32000, -32000, 1, 1, 0, 0, 0, 0);
        try {
            if (menuWindow == 0) return;
            AppendMenu(menu, 0, OpenCommand, "Open Krono Agent");
            AppendMenu(menu, 0x800, 0, null);
            AppendMenu(menu, 0, QuitCommand, "Quit Krono Agent");
            ShowWindow(menuWindow, 4);
            SetForegroundWindow(menuWindow);
            command = TrackPopupMenu(menu, 0x0102, x, y, 0, menuWindow, 0);
            PostMessage(menuWindow, 0, 0, 0);
        } finally {
            if (menuWindow != 0) DestroyWindow(menuWindow);
            DestroyMenu(menu);
        }
        if (command == OpenCommand) open();
        else if (command == QuitCommand) quit();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (IsAvailable) {
            var data = IconData();
            ShellNotifyIcon(DeleteIcon, ref data);
            IsAvailable = false;
        }
        if (hooked) RemoveWindowSubclass(window, windowProc, 1);
        if (icon != 0) DestroyIcon(icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemId;
        public nint BalloonIcon;
    }

    delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint reference);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")] static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint CreateWindowEx(uint extendedStyle, string className, string title,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);
    [DllImport("user32.dll")] static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
}
