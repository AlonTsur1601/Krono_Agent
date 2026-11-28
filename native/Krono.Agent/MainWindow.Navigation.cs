using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Krono_Agent;

public sealed partial class MainWindow
{
    const uint AppCommandMessage = 0x0319;
    const uint XButtonUp = 0x020C, NonClientXButtonUp = 0x00AC;
    const nuint NavigationSubclass = 2;
    NavigationWindowProc? navigationWindowProc;
    MessageHook? navigationMouseHook;

    void InitializeAppNavigation()
    {
        KronoInputMotion.StyleButton(AppBack);
        KronoInputMotion.StyleButton(AppForward);
        AppBack.Click += (_, _) => NavigateHistory(-1);
        AppForward.Click += (_, _) => NavigateHistory(1);
        if (Content is UIElement root) {
            AddShortcut(root, VirtualKey.Left, -1);
            AddShortcut(root, VirtualKey.Right, 1);
        }
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        navigationWindowProc = HandleNavigationCommand;
        if (SetWindowSubclass(window, navigationWindowProc, NavigationSubclass, 0))
            Closed += (_, _) => RemoveWindowSubclass(window, navigationWindowProc, NavigationSubclass);
        navigationMouseHook = (code, operation, address) => {
            if (code >= 0 && operation == 1 && (uint)Marshal.ReadInt32(address, IntPtr.Size) is XButtonUp or NonClientXButtonUp) {
                var message = Marshal.PtrToStructure<NativeMessage>(address);
                if (message.Window == window || IsChild(window, message.Window)) {
                    var button = (message.WParam >> 16) & 0xffff;
                    if (button is 1 or 2 && QueueNavigation(button == 1 ? -1 : 1)) {
                        // Consume the release before WinUI or DefWindowProc can handle it a second time.
                        Marshal.WriteInt32(address, IntPtr.Size, 0);
                    }
                }
            }
            return CallNextHookEx(0, code, operation, address);
        };
        var hook = SetWindowsHookExW(3, navigationMouseHook, 0, GetCurrentThreadId()); // WH_GETMESSAGE, this UI thread only
        if (hook != 0) Closed += (_, _) => UnhookWindowsHookEx(hook);
    }

    void AddShortcut(UIElement root, VirtualKey key, int direction)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Menu };
        accelerator.Invoked += (_, args) => args.Handled = NavigateHistory(direction);
        root.KeyboardAccelerators.Add(accelerator);
    }

    internal void UpdateNavigationButtons(bool back, bool forward)
    {
        AppBack.IsEnabled = back;
        AppForward.IsEnabled = forward;
    }

    bool NavigateHistory(int direction) =>
        RootFrame.Content is MainPage page && page.NavigateHistory(direction);

    bool QueueNavigation(int direction) =>
        (direction < 0 ? AppBack : AppForward).IsEnabled &&
        DispatcherQueue.TryEnqueue(() => NavigateHistory(direction));

    nint HandleNavigationCommand(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        var command = ((uint)lParam >> 16) & 0x0FFF;
        if (message == AppCommandMessage && command is 1 or 2) {
            var direction = command == 1 ? -1 : 1;
            if (QueueNavigation(direction)) return 1;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    delegate nint NavigationWindowProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(nint window, NavigationWindowProc callback, nuint id, nuint reference);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(nint window, NavigationWindowProc callback, nuint id);
    [DllImport("comctl32.dll")] static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct NativeMessage
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    delegate nint MessageHook(int code, nuint wParam, nint lParam);
    [DllImport("user32.dll")] static extern nint SetWindowsHookExW(int id, MessageHook callback, nint module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [DllImport("user32.dll")] static extern bool IsChild(nint parent, nint child);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
