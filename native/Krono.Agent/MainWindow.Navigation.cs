using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Krono_Agent;

public sealed partial class MainWindow
{
    const uint AppCommandMessage = 0x0319;
    const uint XButtonUp = 0x020C;
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
        navigationMouseHook = (code, message, address) => {
            if (code >= 0 && message == XButtonUp && GetForegroundWindow() == window) {
                var click = Marshal.PtrToStructure<MouseHookData>(address);
                var button = click.MouseData >> 16;
                var target = WindowFromPoint(click.Position);
                if (button is 1 or 2 && (target == window || IsChild(window, target)) &&
                    QueueNavigation(button == 1 ? -1 : 1)) return 1;
            }
            return CallNextHookEx(0, code, message, address);
        };
        // WinUI may consume extended mouse messages before forwarding an app command.
        // Listen only while this window is active, and leave clicks in all other windows alone.
        nint hook = 0;
        void RemoveMouseHook()
        {
            if (hook == 0) return;
            UnhookWindowsHookEx(hook);
            hook = 0;
        }
        Activated += (_, args) => {
            if (args.WindowActivationState == WindowActivationState.Deactivated) RemoveMouseHook();
            else if (hook == 0) hook = SetWindowsHookExW(14, navigationMouseHook, GetModuleHandleW(null), 0);
        };
        Closed += (_, _) => RemoveMouseHook();
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
    struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MouseHookData
    {
        public NativePoint Position;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    delegate nint MessageHook(int code, nuint wParam, nint lParam);
    [DllImport("user32.dll")] static extern nint SetWindowsHookExW(int id, MessageHook callback, nint module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [DllImport("user32.dll")] static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern nint WindowFromPoint(NativePoint point);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern nint GetModuleHandleW(string? module);
}
