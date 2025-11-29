using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using FlowVault.Shared.Models;

namespace FlowVault.UI.Controls;

/// <summary>
/// Base class for all draggable tiles
/// </summary>
public abstract class TileBase : UserControl
{
    private bool _isDragging;
    private Windows.Foundation.Point _dragStart;
    private double _startX, _startY;

    public Guid TileId { get; set; }

    public event EventHandler<(double X, double Y)>? PositionChanged;
    public event EventHandler<(double Width, double Height)>? TileSizeChanged;
    public event EventHandler? CloseRequested;

    protected TileBase()
    {
        this.PointerPressed += OnPointerPressed;
        this.PointerMoved += OnPointerMoved;
        this.PointerReleased += OnPointerReleased;
    }

    protected Border CreateTileContainer(FrameworkElement content)
    {
        var container = new Border
        {
            Style = (Style)Application.Current.Resources["TileBaseStyle"],
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = new GridLength(32) },
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
                },
                Children =
                {
                    CreateTitleBar(),
                    content
                }
            }
        };

        Grid.SetRow(content, 1);
        return container;
    }

    private Grid CreateTitleBar()
    {
        var titleBar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var title = new TextBlock
        {
            Text = GetTileTitle(),
            Style = (Style)Application.Current.Resources["TileTitleStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeButton = new Button
        {
            Style = (Style)Application.Current.Resources["IconButtonStyle"],
            Content = new FontIcon { Glyph = "\uE8BB", FontSize = 12 }
        };
        closeButton.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);

        titleBar.Children.Add(title);
        titleBar.Children.Add(closeButton);
        Grid.SetColumn(closeButton, 1);

        return titleBar;
    }

    protected abstract string GetTileTitle();

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStart = e.GetCurrentPoint(Parent as UIElement).Position;
            
            var left = Canvas.GetLeft(this);
            var top = Canvas.GetTop(this);
            _startX = double.IsNaN(left) ? 0 : left;
            _startY = double.IsNaN(top) ? 0 : top;

            CapturePointer(e.Pointer);
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            var current = e.GetCurrentPoint(Parent as UIElement).Position;
            var deltaX = current.X - _dragStart.X;
            var deltaY = current.Y - _dragStart.Y;

            Canvas.SetLeft(this, _startX + deltaX);
            Canvas.SetTop(this, _startY + deltaY);
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleasePointerCapture(e.Pointer);

            var newX = Canvas.GetLeft(this);
            var newY = Canvas.GetTop(this);
            PositionChanged?.Invoke(this, (newX, newY));
        }
    }
}
