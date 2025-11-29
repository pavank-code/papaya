using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FlowVault.UI.Services;
using FlowVault.Shared.Models;

namespace FlowVault.UI.Controls;

/// <summary>
/// Canvas host for draggable, anchored tiles
/// </summary>
public sealed partial class TileHost : UserControl
{
    private BackendClient? _backend;
    private TileManager? _tileManager;
    private readonly Dictionary<Guid, TileBase> _tiles = new();

    public TileHost()
    {
        this.InitializeComponent();
        this.SizeChanged += TileHost_SizeChanged;
    }

    public void Initialize(BackendClient backend)
    {
        _backend = backend;
        _tileManager = new TileManager(backend);

        _tileManager.TileAdded += OnTileAdded;
        _tileManager.TileRemoved += OnTileRemoved;
        _tileManager.TileUpdated += OnTileUpdated;

        _ = LoadTilesAsync();
    }

    private async Task LoadTilesAsync()
    {
        if (_tileManager == null) return;

        await _tileManager.LoadPinnedTilesAsync();
        
        foreach (var pin in _tileManager.GetPinnedTiles())
        {
            CreateTileControl(pin);
        }
    }

    private void OnTileAdded(PinnedTileDto pin)
    {
        DispatcherQueue.TryEnqueue(() => CreateTileControl(pin));
    }

    private void OnTileRemoved(Guid tileId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_tiles.TryGetValue(tileId, out var tile))
            {
                TileCanvas.Children.Remove(tile);
                _tiles.Remove(tileId);
            }
        });
    }

    private void OnTileUpdated(PinnedTileDto pin)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_tiles.TryGetValue(pin.Id, out var tile))
            {
                Canvas.SetLeft(tile, pin.PositionX);
                Canvas.SetTop(tile, pin.PositionY);
                tile.Width = pin.Width;
                tile.Height = pin.Height;
            }
        });
    }

    private void CreateTileControl(PinnedTileDto pin)
    {
        var tile = CreateTileByType(pin);
        if (tile == null) return;

        tile.TileId = pin.Id;
        tile.Width = pin.Width;
        tile.Height = pin.Height;

        Canvas.SetLeft(tile, pin.PositionX);
        Canvas.SetTop(tile, pin.PositionY);

        // Wire up drag events
        tile.PositionChanged += Tile_PositionChanged;
        tile.TileSizeChanged += Tile_TileSizeChanged;
        tile.CloseRequested += Tile_CloseRequested;

        TileCanvas.Children.Add(tile);
        _tiles[pin.Id] = tile;
    }

    private TileBase? CreateTileByType(PinnedTileDto pin)
    {
        return pin.TileType switch
        {
            TileType.FolderSummary => new FolderSummaryTile(_backend!, pin.ReferenceId),
            TileType.FileSummary => new FileSummaryTile(_backend!, pin.ReferenceId),
            TileType.Task => new TaskTile(_backend!, pin.ReferenceId),
            TileType.Calendar => new CalendarTile(_backend!),
            TileType.Assistant => new AssistantTile(_backend!),
            _ => null
        };
    }

    private async void Tile_PositionChanged(object? sender, (double X, double Y) position)
    {
        if (sender is TileBase tile && _tileManager != null)
        {
            // Snap to anchor
            var snapped = _tileManager.SnapToAnchor(position.X, position.Y, ActualWidth, ActualHeight);
            
            Canvas.SetLeft(tile, snapped.X);
            Canvas.SetTop(tile, snapped.Y);

            await _tileManager.UpdateTilePositionAsync(tile.TileId, snapped.X, snapped.Y);
        }
    }

    private async void Tile_TileSizeChanged(object? sender, (double Width, double Height) size)
    {
        if (sender is TileBase tile && _tileManager != null)
        {
            await _tileManager.UpdateTileSizeAsync(tile.TileId, size.Width, size.Height);
        }
    }

    private async void Tile_CloseRequested(object? sender, EventArgs e)
    {
        if (sender is TileBase tile && _tileManager != null)
        {
            await _tileManager.UnpinTileAsync(tile.TileId);
        }
    }

    private void TileHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Re-anchor tiles if container resizes
    }

    public async Task AddTileAsync(TileType type, string? referenceId = null)
    {
        if (_tileManager == null) return;

        // Default position: center
        var x = (ActualWidth - 300) / 2;
        var y = (ActualHeight - 200) / 2;

        await _tileManager.PinTileAsync(type, referenceId, x, y, 300, 200);
    }
}
