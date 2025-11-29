using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using FlowVault.UI.Services;
using FlowVault.Shared.Models;

namespace FlowVault.UI.Controls;

/// <summary>
/// 56px top bar with project selector, search, and actions
/// </summary>
public sealed partial class TopBar : UserControl
{
    private BackendClient? _backend;
    private MainWindow? _mainWindow;
    private List<ProjectDto> _projects = new();

    public TopBar()
    {
        this.InitializeComponent();
    }

    public void Initialize(BackendClient backend, MainWindow mainWindow)
    {
        _backend = backend;
        _mainWindow = mainWindow;
        _ = LoadProjectsAsync();
    }

    private async Task LoadProjectsAsync()
    {
        if (_backend == null) return;

        try
        {
            var projects = await _backend.GetProjectsAsync();
            if (projects != null)
            {
                _projects = projects.ToList();
                ProjectSelector.ItemsSource = _projects;
                
                var activeProject = _projects.FirstOrDefault(p => p.IsActive);
                if (activeProject != null)
                {
                    ProjectSelector.SelectedItem = activeProject;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load projects: {ex.Message}");
        }
    }

    public void SetClickThroughMode(bool isClickThrough)
    {
        ClickThroughIcon.Glyph = isClickThrough ? "\uE8B8" : "\uE8B7";
    }

    private void ProjectSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectSelector.SelectedItem is ProjectDto project && _backend != null)
        {
            _ = _backend.SendRequestAsync<object>("projects.setActive", new() { ["projectId"] = project.Id });
        }
    }

    private async void AddProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Add Project",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };

        var panel = new StackPanel { Spacing = 8 };
        var nameBox = new TextBox { PlaceholderText = "Project Name" };
        var pathBox = new TextBox { PlaceholderText = "Root Path (e.g., C:\\MyProject)" };
        panel.Children.Add(nameBox);
        panel.Children.Add(pathBox);
        dialog.Content = panel;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && _backend != null)
        {
            var project = new ProjectDto
            {
                Name = nameBox.Text,
                RootPath = pathBox.Text
            };

            await _backend.CreateProjectAsync(project);
            await LoadProjectsAsync();
        }
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var query = SearchBox.Text;
            if (!string.IsNullOrWhiteSpace(query))
            {
                // TODO: Trigger search
                System.Diagnostics.Debug.WriteLine($"Search: {query}");
            }
        }
    }

    private void ClickThroughToggle_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.ToggleClickThrough();
    }

    private async void AddTile_Click(object sender, RoutedEventArgs e)
    {
        var menuFlyout = new MenuFlyout();
        
        menuFlyout.Items.Add(new MenuFlyoutItem 
        { 
            Text = "Folder Summary",
            Icon = new FontIcon { Glyph = "\uE8B7" },
            Command = new RelayCommand(() => AddTile(TileType.FolderSummary))
        });
        
        menuFlyout.Items.Add(new MenuFlyoutItem 
        { 
            Text = "File Summary",
            Icon = new FontIcon { Glyph = "\uE8A5" },
            Command = new RelayCommand(() => AddTile(TileType.FileSummary))
        });
        
        menuFlyout.Items.Add(new MenuFlyoutItem 
        { 
            Text = "Task",
            Icon = new FontIcon { Glyph = "\uE73A" },
            Command = new RelayCommand(() => AddTile(TileType.Task))
        });
        
        menuFlyout.Items.Add(new MenuFlyoutItem 
        { 
            Text = "Calendar",
            Icon = new FontIcon { Glyph = "\uE787" },
            Command = new RelayCommand(() => AddTile(TileType.Calendar))
        });
        
        menuFlyout.Items.Add(new MenuFlyoutItem 
        { 
            Text = "Assistant",
            Icon = new FontIcon { Glyph = "\uE8BD" },
            Command = new RelayCommand(() => AddTile(TileType.Assistant))
        });

        menuFlyout.ShowAt(sender as FrameworkElement);
    }

    private void AddTile(TileType type)
    {
        // Notify TileHost to add new tile
        TileRequested?.Invoke(this, type);
    }

    public event EventHandler<TileType>? TileRequested;

    private async void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Settings",
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot
        };

        var panel = new StackPanel { Spacing = 16, MinWidth = 300 };
        
        // API Key Settings
        var apiHeader = new TextBlock { Text = "API Keys", FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        panel.Children.Add(apiHeader);

        var geminiKey = new PasswordBox { PlaceholderText = "Gemini API Key" };
        var openaiKey = new PasswordBox { PlaceholderText = "OpenAI API Key" };
        panel.Children.Add(geminiKey);
        panel.Children.Add(openaiKey);

        // Load existing keys
        if (_backend != null)
        {
            try
            {
                var existingGemini = await _backend.GetApiKeyAsync(LlmProvider.Gemini);
                var existingOpenai = await _backend.GetApiKeyAsync(LlmProvider.OpenAI);
                if (!string.IsNullOrEmpty(existingGemini)) geminiKey.Password = existingGemini;
                if (!string.IsNullOrEmpty(existingOpenai)) openaiKey.Password = existingOpenai;
            }
            catch { }
        }

        var saveButton = new Button { Content = "Save API Keys", HorizontalAlignment = HorizontalAlignment.Right };
        saveButton.Click += async (s, args) =>
        {
            if (_backend != null)
            {
                if (!string.IsNullOrEmpty(geminiKey.Password))
                    await _backend.SaveApiKeyAsync(LlmProvider.Gemini, geminiKey.Password);
                if (!string.IsNullOrEmpty(openaiKey.Password))
                    await _backend.SaveApiKeyAsync(LlmProvider.OpenAI, openaiKey.Password);
            }
        };
        panel.Children.Add(saveButton);

        dialog.Content = panel;
        await dialog.ShowAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }
}

/// <summary>
/// Simple relay command implementation
/// </summary>
public class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}
