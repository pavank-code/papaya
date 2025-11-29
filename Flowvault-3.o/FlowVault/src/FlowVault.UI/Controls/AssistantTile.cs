using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using FlowVault.UI.Services;
using FlowVault.Shared.Models;

namespace FlowVault.UI.Controls;

/// <summary>
/// AI Assistant tile with streaming chat
/// </summary>
public class AssistantTile : TileBase
{
    private readonly BackendClient _backend;
    private ListView? _messagesList;
    private TextBox? _inputBox;
    private Button? _sendButton;
    private readonly List<ChatMessageDisplay> _messages = new();
    private CancellationTokenSource? _streamCts;

    public AssistantTile(BackendClient backend)
    {
        _backend = backend;
        Content = CreateTileContainer(CreateContent());
    }

    protected override string GetTileTitle() => "Assistant";

    private FrameworkElement CreateContent()
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        // Messages list
        _messagesList = new ListView
        {
            ItemTemplate = CreateMessageTemplate(),
            SelectionMode = ListViewSelectionMode.None
        };
        _messagesList.ItemsSource = _messages;
        grid.Children.Add(_messagesList);

        // Input area
        var inputPanel = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Margin = new Thickness(0, 8, 0, 0)
        };

        _inputBox = new TextBox
        {
            PlaceholderText = "Ask a question...",
            Style = (Style)Application.Current.Resources["GlassTextBoxStyle"],
            AcceptsReturn = false
        };
        _inputBox.KeyDown += InputBox_KeyDown;

        _sendButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE724", FontSize = 16 },
            Style = (Style)Application.Current.Resources["IconButtonStyle"],
            Margin = new Thickness(8, 0, 0, 0)
        };
        _sendButton.Click += SendButton_Click;

        inputPanel.Children.Add(_inputBox);
        inputPanel.Children.Add(_sendButton);
        Grid.SetColumn(_sendButton, 1);
        Grid.SetRow(inputPanel, 1);
        grid.Children.Add(inputPanel);

        return grid;
    }

    private DataTemplate CreateMessageTemplate()
    {
        // Simple template - in real implementation would be more sophisticated
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(@"
            <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <StackPanel Margin='0,4'>
                    <TextBlock Text='{Binding Role}' FontWeight='SemiBold' Foreground='#808080' FontSize='11' />
                    <TextBlock Text='{Binding Content}' TextWrapping='Wrap' Foreground='White' />
                </StackPanel>
            </DataTemplate>");
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _ = SendMessageAsync();
        }
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        _ = SendMessageAsync();
    }

    private async Task SendMessageAsync()
    {
        if (_inputBox == null || string.IsNullOrWhiteSpace(_inputBox.Text)) return;

        var userMessage = _inputBox.Text;
        _inputBox.Text = string.Empty;

        // Add user message
        _messages.Add(new ChatMessageDisplay { Role = "You", Content = userMessage });
        RefreshMessagesList();

        // Add assistant placeholder
        var assistantMessage = new ChatMessageDisplay { Role = "Assistant", Content = "" };
        _messages.Add(assistantMessage);
        RefreshMessagesList();

        // Stream response
        _streamCts?.Cancel();
        _streamCts = new CancellationTokenSource();

        try
        {
            var request = new ChatRequestDto
            {
                Messages = _messages.Select(m => new ChatMessageDto
                {
                    Role = m.Role == "You" ? "user" : "assistant",
                    Content = m.Content
                }).ToList(),
                Provider = LlmProvider.Gemini
            };

            await foreach (var chunk in _backend.StreamChatAsync(request, _streamCts.Token))
            {
                assistantMessage.Content += chunk.Token;
                RefreshMessagesList();

                if (chunk.IsComplete)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            assistantMessage.Content = $"Error: {ex.Message}";
            RefreshMessagesList();
        }
    }

    private void RefreshMessagesList()
    {
        if (_messagesList != null)
        {
            _messagesList.ItemsSource = null;
            _messagesList.ItemsSource = _messages;
            
            // Scroll to bottom
            if (_messages.Count > 0)
            {
                _messagesList.ScrollIntoView(_messages[_messages.Count - 1]);
            }
        }
    }
}

public class ChatMessageDisplay
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
