using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace FlowVault.UI.Services;

/// <summary>
/// Global hotkey service for Ctrl+. toggle
/// </summary>
public class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_CONTROL = 0x0002;
    private const int VK_OEM_PERIOD = 0xBE;
    private const int HOTKEY_ID = 1;

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private bool _isRegistered;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotkeyService(Window window)
    {
        _window = window;
        _hwnd = WindowNative.GetWindowHandle(window);
    }

    public bool RegisterHotkey()
    {
        if (_isRegistered) return true;

        // Register Ctrl+.
        _isRegistered = RegisterHotKey(_hwnd, HOTKEY_ID, MOD_CONTROL, VK_OEM_PERIOD);
        
        if (_isRegistered)
        {
            // Hook into message loop
            // Note: In WinUI 3, we need to use a different approach
            // This is a simplified version - full implementation would use SetWindowSubclass
            System.Diagnostics.Debug.WriteLine("Hotkey Ctrl+. registered successfully");
        }

        return _isRegistered;
    }

    public void UnregisterHotkey()
    {
        if (_isRegistered)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _isRegistered = false;
        }
    }

    public void Dispose()
    {
        UnregisterHotkey();
    }
}
