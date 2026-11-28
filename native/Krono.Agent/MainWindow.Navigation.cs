using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Krono_Agent;

public sealed partial class MainWindow
{
    const uint AppCommandMessage = 0x0319;
    const nuint NavigationSubclass = 2;
    NavigationWindowProc? navigationWindowProc;

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

    nint HandleNavigationCommand(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        var command = ((uint)lParam >> 16) & 0x0FFF;
        if (message == AppCommandMessage && command is 1 or 2) {
            var direction = command == 1 ? -1 : 1;
            if ((direction < 0 ? AppBack : AppForward).IsEnabled &&
                DispatcherQueue.TryEnqueue(() => NavigateHistory(direction))) return 1;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    delegate nint NavigationWindowProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(nint window, NavigationWindowProc callback, nuint id, nuint reference);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(nint window, NavigationWindowProc callback, nuint id);
    [DllImport("comctl32.dll")] static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
}
