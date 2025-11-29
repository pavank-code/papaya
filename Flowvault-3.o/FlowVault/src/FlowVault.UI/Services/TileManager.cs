using FlowVault.Shared.Models;

namespace FlowVault.UI.Services;

/// <summary>
/// Manages tile creation, positioning, and lifecycle
/// </summary>
public class TileManager
{
    private readonly BackendClient _backend;
    private readonly List<PinnedTileDto> _pinnedTiles = new();

    public event Action<PinnedTileDto>? TileAdded;
    public event Action<Guid>? TileRemoved;
    public event Action<PinnedTileDto>? TileUpdated;

    public TileManager(BackendClient backend)
    {
        _backend = backend;
    }

    public async Task LoadPinnedTilesAsync(CancellationToken ct = default)
    {
        var tiles = await _backend.GetPinnedTilesAsync(ct);
        if (tiles != null)
        {
            _pinnedTiles.Clear();
            _pinnedTiles.AddRange(tiles);
        }
    }

    public IReadOnlyList<PinnedTileDto> GetPinnedTiles() => _pinnedTiles.AsReadOnly();

    public async Task PinTileAsync(TileType type, string? referenceId, double x, double y, double width, double height, CancellationToken ct = default)
    {
        var tile = new PinnedTileDto
        {
            Id = Guid.NewGuid(),
            TileType = type,
            ReferenceId = referenceId,
            PositionX = x,
            PositionY = y,
            Width = width,
            Height = height
        };

        await _backend.SavePinnedTileAsync(tile, ct);
        _pinnedTiles.Add(tile);
        TileAdded?.Invoke(tile);
    }

    public async Task UnpinTileAsync(Guid tileId, CancellationToken ct = default)
    {
        await _backend.DeletePinnedTileAsync(tileId, ct);
        _pinnedTiles.RemoveAll(t => t.Id == tileId);
        TileRemoved?.Invoke(tileId);
    }

    public async Task UpdateTilePositionAsync(Guid tileId, double x, double y, CancellationToken ct = default)
    {
        var tile = _pinnedTiles.FirstOrDefault(t => t.Id == tileId);
        if (tile == null) return;

        tile.PositionX = x;
        tile.PositionY = y;
        await _backend.SavePinnedTileAsync(tile, ct);
        TileUpdated?.Invoke(tile);
    }

    public async Task UpdateTileSizeAsync(Guid tileId, double width, double height, CancellationToken ct = default)
    {
        var tile = _pinnedTiles.FirstOrDefault(t => t.Id == tileId);
        if (tile == null) return;

        tile.Width = width;
        tile.Height = height;
        await _backend.SavePinnedTileAsync(tile, ct);
        TileUpdated?.Invoke(tile);
    }

    /// <summary>
    /// Snap tile to nearest anchor point
    /// </summary>
    public (double X, double Y) SnapToAnchor(double x, double y, double containerWidth, double containerHeight)
    {
        const double snapThreshold = 50;
        const double margin = 16;

        // Check corners and edges
        var anchors = new (double X, double Y)[]
        {
            (margin, margin),                                      // Top-left
            (containerWidth / 2, margin),                          // Top-center
            (containerWidth - margin, margin),                     // Top-right
            (margin, containerHeight / 2),                         // Middle-left
            (containerWidth - margin, containerHeight / 2),        // Middle-right
            (margin, containerHeight - margin),                    // Bottom-left
            (containerWidth / 2, containerHeight - margin),        // Bottom-center
            (containerWidth - margin, containerHeight - margin)    // Bottom-right
        };

        foreach (var anchor in anchors)
        {
            var distance = Math.Sqrt(Math.Pow(x - anchor.X, 2) + Math.Pow(y - anchor.Y, 2));
            if (distance < snapThreshold)
                return anchor;
        }

        return (x, y);
    }
}
