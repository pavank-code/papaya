using System.Text.Json.Serialization;

namespace FlowVault.Shared.Models;

/// <summary>
/// Task data transfer object
/// </summary>
public class TaskDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int EstimatedMinutes { get; set; } = 60;
    public TaskDifficulty Difficulty { get; set; } = TaskDifficulty.Medium;
    public TaskPriority Importance { get; set; } = TaskPriority.Medium;
    public double AiPriorityScore { get; set; }
    public double Confidence { get; set; } = 0.8;
    public DateTime? DueDate { get; set; }
    public List<string> Dependencies { get; set; } = new();
    public List<SubtaskDto> Subtasks { get; set; } = new();
    public ContextScopeDto? ContextScope { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.NotStarted;
    public int TimeSpentSeconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? PriorityRationale { get; set; }
    public string? ProjectId { get; set; }
}

/// <summary>
/// Subtask DTO
/// </summary>
public class SubtaskDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public int EstimatedMinutes { get; set; } = 15;
}

/// <summary>
/// Task creation DTO
/// </summary>
public class TaskCreateDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int EstimatedMinutes { get; set; } = 60;
    public TaskDifficulty Difficulty { get; set; } = TaskDifficulty.Medium;
    public TaskPriority Importance { get; set; } = TaskPriority.Medium;
    public DateTime? DueDate { get; set; }
    public List<string> Dependencies { get; set; } = new();
    public string? ProjectId { get; set; }
}

/// <summary>
/// Task query parameters
/// </summary>
public class TaskQuery
{
    public string? ProjectId { get; set; }
    public TaskStatus? Status { get; set; }
    public TaskPriority? MinImportance { get; set; }
    public DateTime? DueBefore { get; set; }
    public DateTime? DueAfter { get; set; }
    public int? Limit { get; set; }
    public string? SortBy { get; set; }
    public bool Descending { get; set; } = true;
}

/// <summary>
/// Task priority result
/// </summary>
public class TaskPriorityResult
{
    public string TaskId { get; set; } = string.Empty;
    public double HeuristicScore { get; set; }
    public double? AiScore { get; set; }
    public double FinalScore { get; set; }
    public string Rationale { get; set; } = string.Empty;
}

/// <summary>
/// Context scope DTO
/// </summary>
public class ContextScopeDto
{
    public ContextScope Type { get; set; }
    public string? Path { get; set; }
    public DateTime? LastIndexedAt { get; set; }
}
