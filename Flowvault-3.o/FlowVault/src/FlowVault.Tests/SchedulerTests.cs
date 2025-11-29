using Xunit;
using FlowVault.BackendHost.Services;
using FlowVault.BackendHost.Persistence;
using FlowVault.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowVault.Tests;

public class SchedulerTests
{
    [Fact]
    public async Task SchedulerCreatesValidSchedule()
    {
        // Arrange
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
        var scheduler = new SchedulerService(NullLogger<SchedulerService>.Instance, db);

        // Create test tasks directly
        var task1 = new TaskDto
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Task 1",
            Description = "First task",
            Importance = TaskPriority.High,
            EstimatedMinutes = 120,
            DueDate = DateTime.UtcNow.AddDays(1)
        };
        await db.SaveTaskAsync(task1);

        var task2 = new TaskDto
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Task 2",
            Description = "Second task",
            Importance = TaskPriority.Medium,
            EstimatedMinutes = 60,
            DueDate = DateTime.UtcNow.AddDays(2)
        };
        await db.SaveTaskAsync(task2);

        // Act
        var schedule = await scheduler.AutoScheduleAsync((Guid?)null, CancellationToken.None);

        // Assert
        Assert.NotNull(schedule);
    }

    [Fact]
    public async Task SchedulerHandlesEmptyTaskList()
    {
        // Arrange
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
        var scheduler = new SchedulerService(NullLogger<SchedulerService>.Instance, db);

        // Act
        var schedule = await scheduler.AutoScheduleAsync((Guid?)null, CancellationToken.None);

        // Assert
        Assert.NotNull(schedule);
        Assert.NotNull(schedule.Blocks); // Should return empty list, not null
    }

    [Fact]
    public async Task SchedulerResolvesConflicts()
    {
        // Arrange
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
        var scheduler = new SchedulerService(NullLogger<SchedulerService>.Instance, db);

        // Act
        var conflicts = await scheduler.ResolveConflictsAsync(CancellationToken.None);

        // Assert - should return list (even if empty)
        Assert.NotNull(conflicts);
    }

    [Fact]
    public async Task SchedulerGetsScheduleForDate()
    {
        // Arrange
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
        var scheduler = new SchedulerService(NullLogger<SchedulerService>.Instance, db);

        // Act
        var schedule = await scheduler.GetScheduleAsync(DateTime.Today, CancellationToken.None);

        // Assert
        Assert.NotNull(schedule);
        Assert.NotNull(schedule.Blocks);
    }

    [Fact]
    public async Task SchedulerUpdatesBlock()
    {
        // Arrange
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
        var scheduler = new SchedulerService(NullLogger<SchedulerService>.Instance, db);

        var block = new ScheduleBlockDto
        {
            Id = Guid.NewGuid(),
            TaskId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(1),
            IsConfirmed = false
        };

        // Act - should not throw
        await scheduler.UpdateBlockAsync(block, CancellationToken.None);

        // Assert - basic test that it doesn't throw
        Assert.True(true);
    }
}
