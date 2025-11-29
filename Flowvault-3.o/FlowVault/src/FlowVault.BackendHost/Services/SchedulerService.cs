using Microsoft.Extensions.Logging;
using FlowVault.Shared.Models;
using FlowVault.BackendHost.Persistence;

namespace FlowVault.BackendHost.Services;

/// <summary>
/// Auto-scheduling service using greedy algorithm with task splitting
/// </summary>
public class SchedulerService
{
    private readonly ILogger<SchedulerService> _logger;
    private readonly DatabaseService _database;

    private const int MinBlockMinutes = 30;
    private const int MaxBlockMinutes = 180; // 3 hours

    public SchedulerService(ILogger<SchedulerService> logger, DatabaseService database)
    {
        _logger = logger;
        _database = database;
    }

    /// <summary>
    /// Get schedule for a week
    /// </summary>
    public async Task<ScheduleDto> GetScheduleAsync(DateTime weekStart, CancellationToken ct)
    {
        var weekEnd = weekStart.AddDays(7);
        var blocks = await _database.GetCalendarBlocksAsync(weekStart, weekEnd);

        return new ScheduleDto
        {
            WeekStart = weekStart,
            Blocks = blocks.Select(b => new ScheduleBlockDto
            {
                Id = Guid.TryParse(b.Id, out var id) ? id : Guid.NewGuid(),
                TaskId = Guid.TryParse(b.TaskId, out var taskId) ? taskId : Guid.Empty,
                StartTime = b.StartTime,
                EndTime = b.EndTime,
                IsConfirmed = b.Status == ScheduleBlockStatus.Scheduled
            }).ToList()
        };
    }

    /// <summary>
    /// Auto-schedule tasks for a project
    /// </summary>
    public async Task<ScheduleDto> AutoScheduleAsync(Guid? projectId, CancellationToken ct)
    {
        var query = projectId.HasValue 
            ? new TaskQuery { ProjectId = projectId.Value.ToString(), Status = Shared.Models.TaskStatus.NotStarted }
            : new TaskQuery { Status = Shared.Models.TaskStatus.NotStarted };

        var tasks = await _database.GetTasksAsync(query);
        var taskIds = tasks.Select(t => t.Id).ToList();

        var request = new ScheduleRequest
        {
            TaskIds = taskIds,
            AvailabilityWindows = new List<AvailabilityWindow>(),
            ExistingBlocks = new List<CalendarBlockDto>(),
            ScheduleStart = DateTime.Today,
            ScheduleEnd = DateTime.Today.AddDays(7)
        };

        var result = await AutoScheduleAsync(request, ct);

        return new ScheduleDto
        {
            WeekStart = DateTime.Today,
            Blocks = result.ScheduledBlocks.Select(b => new ScheduleBlockDto
            {
                Id = Guid.TryParse(b.Id, out var id) ? id : Guid.NewGuid(),
                TaskId = Guid.TryParse(b.TaskId, out var taskId) ? taskId : Guid.Empty,
                StartTime = b.StartTime,
                EndTime = b.EndTime,
                IsConfirmed = false
            }).ToList()
        };
    }

    /// <summary>
    /// Update a schedule block
    /// </summary>
    public async Task UpdateBlockAsync(ScheduleBlockDto block, CancellationToken ct)
    {
        var calendarBlock = new CalendarBlockDto
        {
            Id = block.Id.ToString(),
            TaskId = block.TaskId.ToString(),
            Title = "Scheduled Task",
            StartTime = block.StartTime,
            EndTime = block.EndTime,
            Status = block.IsConfirmed ? ScheduleBlockStatus.Scheduled : ScheduleBlockStatus.Proposed
        };

        await _database.SaveCalendarBlockAsync(calendarBlock);
        _logger.LogInformation("Updated schedule block {Id}", block.Id);
    }

    /// <summary>
    /// Resolve scheduling conflicts
    /// </summary>
    public async Task<IReadOnlyList<ScheduleConflictDto>> ResolveConflictsAsync(CancellationToken ct)
    {
        var conflicts = new List<ScheduleConflictDto>();
        var blocks = await _database.GetCalendarBlocksAsync(DateTime.Today, DateTime.Today.AddDays(30));
        var sortedBlocks = blocks.OrderBy(b => b.StartTime).ToList();

        for (int i = 0; i < sortedBlocks.Count - 1; i++)
        {
            var current = sortedBlocks[i];
            var next = sortedBlocks[i + 1];

            if (current.EndTime > next.StartTime)
            {
                // Overlap detected
                var conflict = new ScheduleConflictDto
                {
                    Block1Id = Guid.TryParse(current.Id, out var id1) ? id1 : Guid.Empty,
                    Block2Id = Guid.TryParse(next.Id, out var id2) ? id2 : Guid.Empty,
                    OverlapStart = next.StartTime,
                    OverlapEnd = current.EndTime,
                    Resolution = "Shifted later block"
                };
                conflicts.Add(conflict);

                // Auto-repair: shift next block
                var overlap = current.EndTime - next.StartTime;
                next.StartTime = current.EndTime;
                next.EndTime = next.EndTime.Add(overlap);
                await _database.SaveCalendarBlockAsync(next);
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Auto-schedule tasks into calendar blocks
    /// </summary>
    public async Task<SchedulingResult> AutoScheduleAsync(ScheduleRequest request, CancellationToken ct)
    {
        var result = new SchedulingResult { Success = true };
        
        // Load tasks
        var tasks = new List<TaskDto>();
        foreach (var taskId in request.TaskIds)
        {
            var task = await _database.GetTaskAsync(taskId);
            if (task != null)
            {
                tasks.Add(task);
            }
        }

        if (tasks.Count == 0)
        {
            result.Success = false;
            result.Message = "No tasks found to schedule";
            return result;
        }

        // Sort by urgency (due date) and priority
        tasks = tasks
            .OrderBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenByDescending(t => t.AiPriorityScore)
            .ToList();

        // Generate availability slots
        var slots = GenerateAvailabilitySlots(request);

        // Schedule each task
        foreach (var task in tasks)
        {
            if (ct.IsCancellationRequested) break;

            var scheduled = TryScheduleTask(task, slots, request, result);
            
            if (!scheduled)
            {
                result.UnschedulableTasks.Add(new UnschedulableTask
                {
                    TaskId = task.Id,
                    RequiredMinutes = task.EstimatedMinutes,
                    AvailableMinutes = slots.Sum(s => s.RemainingMinutes),
                    Reason = "Insufficient time slots available"
                });
            }
        }

        // Validate for overlaps and repair if needed
        ValidateAndRepair(result);

        // Persist scheduled blocks
        foreach (var block in result.ScheduledBlocks)
        {
            await _database.SaveCalendarBlockAsync(block);
        }

        result.Message = result.ScheduledBlocks.Count > 0
            ? $"Scheduled {result.ScheduledBlocks.Count} blocks for {tasks.Count - result.UnschedulableTasks.Count} tasks"
            : "No tasks could be scheduled";

        _logger.LogInformation("{Message}", result.Message);

        return result;
    }

    /// <summary>
    /// Get calendar blocks for a date range
    /// </summary>
    public async Task<IEnumerable<CalendarBlockDto>> GetCalendarBlocksAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        return await _database.GetCalendarBlocksAsync(start, end);
    }

    /// <summary>
    /// Update block status
    /// </summary>
    public async Task<CalendarBlockDto> UpdateBlockStatusAsync(string blockId, ScheduleBlockStatus status, CancellationToken ct)
    {
        var blocks = await _database.GetCalendarBlocksAsync(DateTime.MinValue, DateTime.MaxValue);
        var block = blocks.FirstOrDefault(b => b.Id == blockId);
        
        if (block != null)
        {
            block.Status = status;
            await _database.SaveCalendarBlockAsync(block);
        }

        return block ?? new CalendarBlockDto { Id = blockId, Status = status };
    }

    #region Scheduling Algorithm

    private class TimeSlot
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int TotalMinutes => (int)(End - Start).TotalMinutes;
        public int UsedMinutes { get; set; }
        public int RemainingMinutes => TotalMinutes - UsedMinutes;
        public List<(DateTime start, DateTime end)> UsedRanges { get; } = new();
    }

    private List<TimeSlot> GenerateAvailabilitySlots(ScheduleRequest request)
    {
        var slots = new List<TimeSlot>();
        var current = request.ScheduleStart.Date;

        while (current <= request.ScheduleEnd)
        {
            var dayOfWeek = current.DayOfWeek;
            var windows = request.AvailabilityWindows
                .Where(w => w.DayOfWeek == dayOfWeek)
                .ToList();

            if (windows.Count == 0)
            {
                // Default: 9 AM - 5 PM on weekdays
                if (dayOfWeek != DayOfWeek.Saturday && dayOfWeek != DayOfWeek.Sunday)
                {
                    windows.Add(new AvailabilityWindow
                    {
                        DayOfWeek = dayOfWeek,
                        StartTime = TimeSpan.FromHours(9),
                        EndTime = TimeSpan.FromHours(17)
                    });
                }
            }

            foreach (var window in windows)
            {
                var slotStart = current.Add(window.StartTime);
                var slotEnd = current.Add(window.EndTime);

                // Skip if slot is in the past
                if (slotEnd <= DateTime.Now) continue;
                if (slotStart < DateTime.Now) slotStart = DateTime.Now;

                // Check against existing blocks
                var slot = new TimeSlot { Start = slotStart, End = slotEnd };
                
                foreach (var existing in request.ExistingBlocks)
                {
                    if (existing.StartTime < slotEnd && existing.EndTime > slotStart)
                    {
                        // Overlap - mark as used
                        var overlapStart = existing.StartTime > slotStart ? existing.StartTime : slotStart;
                        var overlapEnd = existing.EndTime < slotEnd ? existing.EndTime : slotEnd;
                        slot.UsedMinutes += (int)(overlapEnd - overlapStart).TotalMinutes;
                        slot.UsedRanges.Add((overlapStart, overlapEnd));
                    }
                }

                if (slot.RemainingMinutes >= request.MinBlockMinutes)
                {
                    slots.Add(slot);
                }
            }

            current = current.AddDays(1);
        }

        return slots;
    }

    private bool TryScheduleTask(TaskDto task, List<TimeSlot> slots, ScheduleRequest request, SchedulingResult result)
    {
        var remainingMinutes = task.EstimatedMinutes;
        var blockIndex = 1;

        foreach (var slot in slots.Where(s => s.RemainingMinutes >= request.MinBlockMinutes))
        {
            if (remainingMinutes <= 0) break;

            // Find free range in slot
            var freeRanges = GetFreeRanges(slot);
            
            foreach (var (freeStart, freeEnd) in freeRanges)
            {
                if (remainingMinutes <= 0) break;

                var availableMinutes = (int)(freeEnd - freeStart).TotalMinutes;
                if (availableMinutes < request.MinBlockMinutes) continue;

                // Determine block size
                var blockMinutes = Math.Min(
                    Math.Min(remainingMinutes, availableMinutes),
                    MaxBlockMinutes
                );

                // Ensure minimum block size
                if (blockMinutes < request.MinBlockMinutes && remainingMinutes >= request.MinBlockMinutes)
                {
                    continue;
                }

                // Create block
                var block = new CalendarBlockDto
                {
                    Id = Guid.NewGuid().ToString(),
                    TaskId = task.Id,
                    Title = remainingMinutes <= blockMinutes 
                        ? task.Title 
                        : $"{task.Title} (Part {blockIndex})",
                    StartTime = freeStart,
                    EndTime = freeStart.AddMinutes(blockMinutes),
                    Status = ScheduleBlockStatus.Proposed
                };

                result.ScheduledBlocks.Add(block);
                
                // Update slot
                slot.UsedMinutes += blockMinutes;
                slot.UsedRanges.Add((block.StartTime, block.EndTime));

                remainingMinutes -= blockMinutes;
                blockIndex++;
            }
        }

        return remainingMinutes <= 0;
    }

    private List<(DateTime start, DateTime end)> GetFreeRanges(TimeSlot slot)
    {
        var ranges = new List<(DateTime, DateTime)>();
        var current = slot.Start;

        var sortedUsed = slot.UsedRanges.OrderBy(r => r.start).ToList();

        foreach (var (usedStart, usedEnd) in sortedUsed)
        {
            if (current < usedStart)
            {
                ranges.Add((current, usedStart));
            }
            current = usedEnd > current ? usedEnd : current;
        }

        if (current < slot.End)
        {
            ranges.Add((current, slot.End));
        }

        return ranges;
    }

    private void ValidateAndRepair(SchedulingResult result)
    {
        var blocks = result.ScheduledBlocks.OrderBy(b => b.StartTime).ToList();

        for (int i = 0; i < blocks.Count - 1; i++)
        {
            var current = blocks[i];
            var next = blocks[i + 1];

            if (current.EndTime > next.StartTime)
            {
                // Overlap detected
                result.Conflicts.Add(new ScheduleConflict
                {
                    Block1Id = current.Id,
                    Block2Id = next.Id,
                    OverlapStart = next.StartTime,
                    OverlapEnd = current.EndTime,
                    Resolution = "Shifted later block"
                });

                // Repair: shift the next block
                var overlap = (int)(current.EndTime - next.StartTime).TotalMinutes;
                next.StartTime = current.EndTime;
                next.EndTime = next.EndTime.AddMinutes(overlap);
            }
        }
    }

    #endregion
}
