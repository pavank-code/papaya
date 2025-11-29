namespace FlowVault.Shared.Models;

/// <summary>
/// Project configuration
/// </summary>
public class ProjectConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Branch { get; set; }
    public DateTime? LastIndexedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<string> IgnorePatterns { get; set; } = new() { "bin", "obj", "node_modules", ".git" };
    public List<string> IncludeExtensions { get; set; } = new() { ".cs", ".ts", ".js", ".py", ".json", ".xaml" };
}

/// <summary>
/// Index status response
/// </summary>
public class IndexStatus
{
    public string ProjectId { get; set; } = string.Empty;
    public IndexingStatus Status { get; set; }
    public int TotalFiles { get; set; }
    public int IndexedFiles { get; set; }
    public int FailedFiles { get; set; }
    public double ProgressPercent => TotalFiles > 0 ? (IndexedFiles * 100.0 / TotalFiles) : 0;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CurrentFile { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Schedule request
/// </summary>
public class ScheduleRequest
{
    public List<string> TaskIds { get; set; } = new();
    public List<AvailabilityWindow> AvailabilityWindows { get; set; } = new();
    public List<CalendarBlockDto> ExistingBlocks { get; set; } = new();
    public DateTime ScheduleStart { get; set; } = DateTime.Today;
    public DateTime ScheduleEnd { get; set; } = DateTime.Today.AddDays(7);
    public int MinBlockMinutes { get; set; } = 30;
}

/// <summary>
/// Availability window
/// </summary>
public class AvailabilityWindow
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

/// <summary>
/// Calendar block
/// </summary>
public class CalendarBlockDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationMinutes => (int)(EndTime - StartTime).TotalMinutes;
    public ScheduleBlockStatus Status { get; set; }
    public bool IsExternal { get; set; } // From external calendar
}

/// <summary>
/// Scheduling result
/// </summary>
public class SchedulingResult
{
    public bool Success { get; set; }
    public List<CalendarBlockDto> ScheduledBlocks { get; set; } = new();
    public List<UnschedulableTask> UnschedulableTasks { get; set; } = new();
    public List<ScheduleConflict> Conflicts { get; set; } = new();
    public string? Message { get; set; }
}

/// <summary>
/// Unschedulable task info
/// </summary>
public class UnschedulableTask
{
    public string TaskId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int RequiredMinutes { get; set; }
    public int AvailableMinutes { get; set; }
}

/// <summary>
/// Schedule conflict
/// </summary>
public class ScheduleConflict
{
    public string Block1Id { get; set; } = string.Empty;
    public string Block2Id { get; set; } = string.Empty;
    public DateTime OverlapStart { get; set; }
    public DateTime OverlapEnd { get; set; }
    public string Resolution { get; set; } = string.Empty;
}

/// <summary>
/// Pinned tile configuration
/// </summary>
public class PinnedTileDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TileId { get; set; } = string.Empty;
    public TileType TileType { get; set; }
    public string? ReferenceId { get; set; }
    public ContextScopeDto? Scope { get; set; }
    public DockRegion DockRegion { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 200;
    public bool IsPinned { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// API key configuration
/// </summary>
public class ApiKeyDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public LlmProvider Provider { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? EncryptedKey { get; set; }
    public string? MaskedKey { get; set; } // For display: "sk-...abc"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastTestedAt { get; set; }
    public bool? LastTestSuccess { get; set; }
}

/// <summary>
/// API key test result
/// </summary>
public class ApiKeyTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ProviderError { get; set; }
    public int? ResponseTimeMs { get; set; }
    public string? ModelName { get; set; }
}

/// <summary>
/// Workflow graph node
/// </summary>
public class WorkflowNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 60;
    public string? Color { get; set; }
    public TaskStatus Status { get; set; }
}

/// <summary>
/// Workflow graph edge
/// </summary>
public class WorkflowEdgeDto
{
    public string Id { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string? Label { get; set; }
}

/// <summary>
/// Workflow graph
/// </summary>
public class WorkflowGraphDto
{
    public List<WorkflowNodeDto> Nodes { get; set; } = new();
    public List<WorkflowEdgeDto> Edges { get; set; } = new();
}

/// <summary>
/// Project DTO for CRUD operations
/// </summary>
public class ProjectDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Task update DTO
/// </summary>
public class TaskUpdateDto
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public TaskPriority? Priority { get; set; }
    public TaskStatus? Status { get; set; }
    public int? EstimatedHours { get; set; }
    public DateTime? DeadlineDate { get; set; }
}

/// <summary>
/// Schedule DTO for week view
/// </summary>
public class ScheduleDto
{
    public DateTime WeekStart { get; set; }
    public List<ScheduleBlockDto> Blocks { get; set; } = new();
}

/// <summary>
/// Schedule block DTO
/// </summary>
public class ScheduleBlockDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsConfirmed { get; set; }
}

/// <summary>
/// Schedule conflict DTO
/// </summary>
public class ScheduleConflictDto
{
    public Guid Block1Id { get; set; }
    public Guid Block2Id { get; set; }
    public DateTime OverlapStart { get; set; }
    public DateTime OverlapEnd { get; set; }
    public string Resolution { get; set; } = string.Empty;
}

/// <summary>
/// Calendar event DTO
/// </summary>
public class CalendarEventDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? Color { get; set; }
}
