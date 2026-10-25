using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Sanduhr.App.Tests;

/// <summary>
/// The render harness's foreground rule for tests (SettingsPassScenario: NeverActivate, RouteKeyboard, GuardForeground).
/// A window shown through it is off screen and can never become the foreground window; <see cref="Check"/> hands the
/// foreground back and fails the test if it ever did.
/// </summary>
internal sealed class NoActivate
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000L, WS_EX_TOOLWINDOW = 0x80L;

    private readonly IntPtr _before = GetForegroundWindow();
    private readonly IntPtr _hwnd;

    private NoActivate(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>Off screen, never activating: the extended styles go on before the first Show.</summary>
    public static NoActivate Show(Window window)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        var guard = new NoActivate(hwnd);
        window.Show();
        guard.Check();
        return guard;
    }

    /// <summary>Lets WPF route keyboard focus inside the window: WM_ACTIVATE and WM_SETFOCUS sent to it, never SetForegroundWindow.</summary>
    public void RouteKeyboard()
    {
        const int WM_ACTIVATE = 0x0006, WM_SETFOCUS = 0x0007;
        SendMessage(_hwnd, WM_ACTIVATE, (IntPtr)1, IntPtr.Zero);
        SendMessage(_hwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
        Check();
    }

    /// <summary>Fails the test, after handing the foreground back, if the window became the foreground window.</summary>
    public void Check()
    {
        if (GetForegroundWindow() != _hwnd)
            return;
        SetForegroundWindow(_before);
        Assert.Fail("The test window became the foreground window; the foreground was handed back.");
    }
}
