using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FlowVault.UI.Services;
using FlowVault.Shared.Models;

namespace FlowVault.UI.Controls;

/// <summary>
/// Tile displaying file summary with code overview
/// </summary>
public class FileSummaryTile : TileBase
{
    private readonly BackendClient _backend;
    private readonly string? _filePath;
    private TextBlock? _summaryText;
    private ItemsControl? _functionsList;

    public FileSummaryTile(BackendClient backend, string? filePath)
    {
        _backend = backend;
        _filePath = filePath;
        
        Content = CreateTileContainer(CreateContent());
        _ = LoadDataAsync();
    }

    protected override string GetTileTitle() => "File Summary";

    private FrameworkElement CreateContent()
    {
        var panel = new StackPanel { Spacing = 8 };

        _summaryText = new TextBlock
        {
            Style = (Style)Application.Current.Resources["TileBodyStyle"],
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(_summaryText);

        var functionsHeader = new TextBlock
        {
            Text = "Functions",
            Style = (Style)Application.Current.Resources["TileSubtitleStyle"],
            Margin = new Thickness(0, 8, 0, 4)
        };
        panel.Children.Add(functionsHeader);

        _functionsList = new ItemsControl();
        panel.Children.Add(_functionsList);

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private async Task LoadDataAsync()
    {
        if (string.IsNullOrEmpty(_filePath)) return;

        try
        {
            var file = await _backend.SendRequestAsync<FileSummaryDto>("files.get", new() { ["filePath"] = _filePath });
            if (file != null)
            {
                if (_summaryText != null)
                {
                    _summaryText.Text = $"{System.IO.Path.GetFileName(file.FilePath)}\n" +
                                        $"Language: {file.Language}\n" +
                                        $"Lines: {file.LineCount}\n" +
                                        $"Hotspot: {file.HotspotScore:F2}";
                }

                if (_functionsList != null && file.Functions != null)
                {
                    var items = file.Functions.Select(f => new TextBlock
                    {
                        Text = $"• {f}",
                        Style = (Style)Application.Current.Resources["TileBodyStyle"]
                    }).ToList();

                    _functionsList.ItemsSource = items;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load file: {ex.Message}");
        }
    }
}
