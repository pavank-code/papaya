using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using FlowVault.UI.Services;

namespace FlowVault.UI;

/// <summary>
/// Main overlay window with glassmorphic design
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly BackendClient _backend;
    private readonly AppWindow _appWindow;
    private bool _isClickThrough;

    public MainWindow(BackendClient backend)
    {
        _backend = backend;
        this.InitializeComponent();

        // Get the AppWindow for extended customization
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Configure window
        ConfigureWindow();

        // Initialize controls
        TopBar.Initialize(_backend, this);
        TileHost.Initialize(_backend);
    }

    private void ConfigureWindow()
    {
        // Set title bar
        _appWindow.Title = "Flow Vault";

        // Make window always on top WITH visible title bar
        var presenter = _appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
            // KEEP the title bar so window is visible and interactable
            presenter.SetBorderAndTitleBar(true, true);
        }

        // Size and position
        _appWindow.Resize(new Windows.Graphics.SizeInt32(1200, 800));
        CenterWindow();
        
        // Force window to show and bring to front
        _appWindow.Show(true);
    }

    private void CenterWindow()
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var x = (workArea.Width - 1200) / 2;
        var y = (workArea.Height - 800) / 2;

        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    public void ToggleClickThrough()
    {
        _isClickThrough = !_isClickThrough;

        var hwnd = WindowNative.GetWindowHandle(this);

        if (_isClickThrough)
        {
            // Make window click-through using WS_EX_TRANSPARENT
            NativeMethods.SetWindowClickThrough(hwnd, true);
            TopBar.SetClickThroughMode(true);
        }
        else
        {
            // Restore normal interaction
            NativeMethods.SetWindowClickThrough(hwnd, false);
            TopBar.SetClickThroughMode(false);
        }
    }

    public bool IsClickThrough => _isClickThrough;
}
