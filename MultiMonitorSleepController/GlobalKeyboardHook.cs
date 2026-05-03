using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MultiMonitorSleepController;

public sealed class GlobalKeyboardHook : IDisposable
{
    private readonly Action _onKeyDown;

    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;

    private IntPtr _hookHandle;

    public GlobalKeyboardHook(Action onKeyDown)
    {
        _onKeyDown = onKeyDown;
        _hookProc = HookCallback;

        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _hookProc,
            IntPtr.Zero,
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install global keyboard hook.");
        }
    }

    public void Dispose()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 &&
            (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            _onKeyDown();
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}
