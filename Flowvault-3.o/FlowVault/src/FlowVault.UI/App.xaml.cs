using Microsoft.UI.Xaml;
using FlowVault.UI.Services;
using System.Diagnostics;
using System.IO;

namespace FlowVault.UI;

/// <summary>
/// Flow Vault - WinUI 3 Application Entry Point
/// </summary>
public partial class App : Application
{
    private Window? _mainWindow;
    private BackendClient? _backendClient;
    private HotkeyService? _hotkeyService;
    private Process? _backendProcess;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += App_UnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Start backend process if not already running
            await StartBackendHostAsync();

            // Initialize backend client
            _backendClient = new BackendClient();
            Backend = _backendClient;

            try
            {
                await _backendClient.ConnectAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to connect to backend: {ex.Message}");
                // Continue anyway - UI can work in limited mode
            }

            // Create main window
            _mainWindow = new MainWindow(_backendClient);
            _mainWindow.Activate();

            // Initialize global hotkey service
            _hotkeyService = new HotkeyService(_mainWindow);
            _hotkeyService.RegisterHotkey();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch app: {ex.Message}");
            // Create a simple error window
            _mainWindow = new MainWindow(_backendClient!);
            _mainWindow.Activate();
        }
    }

    private async Task StartBackendHostAsync()
    {
        // Check if backend is already running
        var existingProcesses = Process.GetProcessesByName("FlowVault.BackendHost");
        if (existingProcesses.Length > 0)
        {
            Debug.WriteLine("BackendHost already running");
            return;
        }

        // Find the backend executable relative to the UI executable
        var uiExePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(uiExePath))
            return;

        var uiDir = Path.GetDirectoryName(uiExePath)!;
        
        // Try multiple possible locations for BackendHost
        var possiblePaths = new[]
        {
            // Same directory (if published together)
            Path.Combine(uiDir, "FlowVault.BackendHost.exe"),
            // Sibling directory structure (dev build)
            Path.Combine(uiDir, "..", "..", "..", "..", "FlowVault.BackendHost", "bin", "x64", "Debug", "net8.0-windows", "FlowVault.BackendHost.exe"),
            Path.Combine(uiDir, "..", "..", "..", "..", "FlowVault.BackendHost", "bin", "Debug", "net8.0-windows", "FlowVault.BackendHost.exe"),
        };

        string? backendPath = null;
        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                backendPath = fullPath;
                break;
            }
        }

        if (backendPath == null)
        {
            Debug.WriteLine("Could not find BackendHost executable");
            return;
        }

        try
        {
            _backendProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = backendPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            _backendProcess.Start();

            // Give backend time to start and create the named pipe
            await Task.Delay(1500);
            Debug.WriteLine($"Started BackendHost from: {backendPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to start BackendHost: {ex.Message}");
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"Unhandled exception: {e.Message}");
        e.Handled = true;
    }

    public static BackendClient? Backend { get; private set; }
}
