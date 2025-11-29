using Xunit;
using FlowVault.BackendHost.Persistence;
using FlowVault.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowVault.Tests;

public class PinPersistenceTests
{
    [Fact]
    public async Task CanSaveAndRetrievePinnedTile()
    {
        // Arrange
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
        var tile = new PinnedTileDto
        {
            Id = Guid.NewGuid(),
            TileType = TileType.Task,
            ReferenceId = Guid.NewGuid().ToString(),
            PositionX = 100,
            PositionY = 200,
            Width = 300,
            Height = 250
        };

        // Act
        await db.SavePinnedTileAsync(tile);
        var retrieved = await db.GetPinnedTilesAsync();

        // Assert
        var found = retrieved.FirstOrDefault(t => t.Id == tile.Id);
        Assert.NotNull(found);
        Assert.Equal(tile.TileType, found.TileType);
        Assert.Equal(tile.ReferenceId, found.ReferenceId);
        Assert.Equal(tile.PositionX, found.PositionX);
        Assert.Equal(tile.PositionY, found.PositionY);
        Assert.Equal(tile.Width, found.Width);
        Assert.Equal(tile.Height, found.Height);
    }

    [Fact]
    public async Task CanUpdatePinnedTilePosition()
    {
        // Arrange
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
        var tile = new PinnedTileDto
        {
            Id = Guid.NewGuid(),
            TileType = TileType.Assistant,
            PositionX = 0,
            PositionY = 0,
            Width = 400,
            Height = 300
        };

        await db.SavePinnedTileAsync(tile);

        // Act - update position
        tile.PositionX = 500;
        tile.PositionY = 300;
        await db.SavePinnedTileAsync(tile);

        // Assert
        var retrieved = await db.GetPinnedTilesAsync();
        var found = retrieved.FirstOrDefault(t => t.Id == tile.Id);
        
        Assert.NotNull(found);
        Assert.Equal(500, found.PositionX);
        Assert.Equal(300, found.PositionY);
    }

    [Fact]
    public async Task CanDeletePinnedTile()
    {
        // Arrange
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
        var tile = new PinnedTileDto
        {
            Id = Guid.NewGuid(),
            TileType = TileType.Calendar,
            PositionX = 50,
            PositionY = 50,
            Width = 200,
            Height = 200
        };

        await db.SavePinnedTileAsync(tile);

        // Act
        await db.DeletePinnedTileAsync(tile.Id);

        // Assert
        var retrieved = await db.GetPinnedTilesAsync();
        var found = retrieved.FirstOrDefault(t => t.Id == tile.Id);
        Assert.Null(found);
    }

    [Fact]
    public async Task MultipleTilesPersistIndependently()
    {
        // Arrange
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
        
        var tiles = new[]
        {
            new PinnedTileDto { Id = Guid.NewGuid(), TileType = TileType.FolderSummary, PositionX = 0, PositionY = 0, Width = 200, Height = 200 },
            new PinnedTileDto { Id = Guid.NewGuid(), TileType = TileType.FileSummary, PositionX = 220, PositionY = 0, Width = 200, Height = 200 },
            new PinnedTileDto { Id = Guid.NewGuid(), TileType = TileType.Task, PositionX = 440, PositionY = 0, Width = 200, Height = 200 }
        };

        // Act
        foreach (var tile in tiles)
        {
            await db.SavePinnedTileAsync(tile);
        }

        var retrieved = await db.GetPinnedTilesAsync();

        // Assert
        foreach (var tile in tiles)
        {
            Assert.Contains(retrieved, t => t.Id == tile.Id);
        }
    }

    [Fact]
    public async Task TileTypesPreservedCorrectly()
    {
        // Arrange
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);

        var tileTypes = new[] 
        { 
            TileType.FolderSummary, 
            TileType.FileSummary, 
            TileType.Task, 
            TileType.Calendar, 
            TileType.Assistant 
        };

        foreach (var type in tileTypes)
        {
            var tile = new PinnedTileDto
            {
                Id = Guid.NewGuid(),
                TileType = type,
                PositionX = 0,
                PositionY = 0,
                Width = 100,
                Height = 100
            };

            // Act
            await db.SavePinnedTileAsync(tile);
            var retrieved = await db.GetPinnedTilesAsync();
            var found = retrieved.FirstOrDefault(t => t.Id == tile.Id);

            // Assert
            Assert.NotNull(found);
            Assert.Equal(type, found.TileType);

            // Cleanup
            await db.DeletePinnedTileAsync(tile.Id);
        }
    }
}
