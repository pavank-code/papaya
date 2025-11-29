using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FlowVault.UI.Services;
using FlowVault.Shared.Models;

namespace FlowVault.UI.Controls;

/// <summary>
/// Tile displaying task details with priority indicator
/// </summary>
public class TaskTile : TileBase
{
    private readonly BackendClient _backend;
    private readonly string? _taskId;
    private TextBlock? _titleText;
    private TextBlock? _descriptionText;
    private Border? _priorityBadge;
    private Border? _statusBadge;

    public TaskTile(BackendClient backend, string? taskId)
    {
        _backend = backend;
        _taskId = taskId;
        
        Content = CreateTileContainer(CreateContent());
        _ = LoadDataAsync();
    }

    protected override string GetTileTitle() => "Task";

    private FrameworkElement CreateContent()
    {
        var panel = new StackPanel { Spacing = 8 };

        // Title
        _titleText = new TextBlock
        {
            Style = (Style)Application.Current.Resources["TileTitleStyle"],
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(_titleText);

        // Badges row
        var badgesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        
        _priorityBadge = new Border { Style = (Style)Application.Current.Resources["PriorityMediumStyle"] };
        _statusBadge = new Border { Style = (Style)Application.Current.Resources["StatusNotStartedStyle"] };
        
        badgesRow.Children.Add(_priorityBadge);
        badgesRow.Children.Add(_statusBadge);
        panel.Children.Add(badgesRow);

        // Description
        _descriptionText = new TextBlock
        {
            Style = (Style)Application.Current.Resources["TileBodyStyle"],
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3
        };
        panel.Children.Add(_descriptionText);

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private async Task LoadDataAsync()
    {
        if (string.IsNullOrEmpty(_taskId)) return;

        try
        {
            var tasks = await _backend.GetTasksAsync();
            var task = tasks?.FirstOrDefault(t => t.Id == _taskId);
            
            if (task != null)
            {
                if (_titleText != null)
                    _titleText.Text = task.Title;

                if (_descriptionText != null)
                    _descriptionText.Text = task.Description;

                if (_priorityBadge != null)
                {
                    _priorityBadge.Style = task.Importance switch
                    {
                        TaskPriority.High => (Style)Application.Current.Resources["PriorityHighStyle"],
                        TaskPriority.Medium => (Style)Application.Current.Resources["PriorityMediumStyle"],
                        _ => (Style)Application.Current.Resources["PriorityLowStyle"]
                    };
                    _priorityBadge.Child = new TextBlock
                    {
                        Text = task.Importance.ToString(),
                        Style = (Style)Application.Current.Resources["TileSubtitleStyle"]
                    };
                }

                if (_statusBadge != null)
                {
                    _statusBadge.Style = task.Status switch
                    {
                        FlowVault.Shared.Models.TaskStatus.InProgress => (Style)Application.Current.Resources["StatusInProgressStyle"],
                        FlowVault.Shared.Models.TaskStatus.Completed => (Style)Application.Current.Resources["StatusCompletedStyle"],
                        _ => (Style)Application.Current.Resources["StatusNotStartedStyle"]
                    };
                    _statusBadge.Child = new TextBlock
                    {
                        Text = task.Status.ToString(),
                        Style = (Style)Application.Current.Resources["TileSubtitleStyle"]
                    };
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load task: {ex.Message}");
        }
    }
}
