using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FlowVault.UI.Services;
using FlowVault.Shared.Models;

namespace FlowVault.UI.Controls;

/// <summary>
/// Tile displaying folder summary with file tree
/// </summary>
public class FolderSummaryTile : TileBase
{
    private readonly BackendClient _backend;
    private readonly string? _folderPath;
    private TreeView? _treeView;

    public FolderSummaryTile(BackendClient backend, string? folderPath)
    {
        _backend = backend;
        _folderPath = folderPath;
        
        Content = CreateTileContainer(CreateContent());
        _ = LoadDataAsync();
    }

    protected override string GetTileTitle() => "Folder Summary";

    private FrameworkElement CreateContent()
    {
        var panel = new StackPanel { Spacing = 8 };

        _treeView = new TreeView();
        panel.Children.Add(_treeView);

        return panel;
    }

    private async Task LoadDataAsync()
    {
        if (string.IsNullOrEmpty(_folderPath)) return;

        try
        {
            var files = await _backend.GetFileSummariesAsync(_folderPath);
            if (files != null && _treeView != null)
            {
                foreach (var file in files)
                {
                    var item = new TreeViewNode
                    {
                        Content = new TextBlock
                        {
                            Text = System.IO.Path.GetFileName(file.FilePath),
                            Style = (Style)Application.Current.Resources["TileBodyStyle"]
                        }
                    };
                    _treeView.RootNodes.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load folder: {ex.Message}");
        }
    }
}
