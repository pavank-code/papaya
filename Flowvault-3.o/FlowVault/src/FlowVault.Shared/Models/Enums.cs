namespace FlowVault.Shared.Models;

/// <summary>
/// Task priority levels
/// </summary>
public enum TaskPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// Task difficulty levels
/// </summary>
public enum TaskDifficulty
{
    Trivial = 1,
    Easy = 2,
    Medium = 3,
    Hard = 4,
    Complex = 5
}

/// <summary>
/// Task status
/// </summary>
public enum TaskStatus
{
    NotStarted,
    InProgress,
    Blocked,
    Completed,
    Cancelled
}

/// <summary>
/// Tile types
/// </summary>
public enum TileType
{
    FolderSummary,
    FileSummary,
    Task,
    Calendar,
    Assistant,
    Settings
}

/// <summary>
/// Chat message roles
/// </summary>
public enum ChatRole
{
    System,
    User,
    Assistant
}

/// <summary>
/// LLM provider types
/// </summary>
public enum LlmProvider
{
    Mock,
    Gemini,
    OpenAI,
    Anthropic,
    Custom
}

/// <summary>
/// Context scope types
/// </summary>
public enum ContextScope
{
    Global,
    Project,
    Folder,
    File,
    Task,
    Selection
}

/// <summary>
/// Dock regions for pinned tiles
/// </summary>
public enum DockRegion
{
    Floating,
    Left,
    Right,
    Center
}

/// <summary>
/// Index status
/// </summary>
public enum IndexingStatus
{
    Idle,
    Running,
    Paused,
    Failed,
    Completed
}

/// <summary>
/// Scheduling block status
/// </summary>
public enum ScheduleBlockStatus
{
    Proposed,
    Scheduled,
    Accepted,
    Rejected,
    Completed,
    Missed
}
