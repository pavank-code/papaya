using Microsoft.Extensions.Logging;
using FlowVault.Shared.Models;
using FlowVault.BackendHost.Persistence;

namespace FlowVault.BackendHost.Services;

/// <summary>
/// Task management service with priority scoring
/// </summary>
public class TaskService
{
    private readonly ILogger<TaskService> _logger;
    private readonly DatabaseService _database;
    private readonly LlmService _llmService;

    // Priority weights
    private const double ImportanceWeight = 0.50;
    private const double DeadlineWeight = 0.15;
    private const double DifficultyInverseWeight = 0.15;
    private const double SizeInverseWeight = 0.10;
    private const double RiskBoostWeight = 0.10;

    public TaskService(ILogger<TaskService> logger, DatabaseService database, LlmService llmService)
    {
        _logger = logger;
        _database = database;
        _llmService = llmService;
    }

    #region CRUD Operations

    public async Task<TaskDto> CreateTaskAsync(TaskCreateDto dto, CancellationToken ct)
    {
        var task = new TaskDto
        {
            Id = Guid.NewGuid().ToString(),
            Title = dto.Title,
            Description = dto.Description,
            EstimatedMinutes = dto.EstimatedMinutes,
            Difficulty = dto.Difficulty,
            Importance = dto.Importance,
            DueDate = dto.DueDate,
            Dependencies = dto.Dependencies,
            ProjectId = dto.ProjectId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Compute initial priority
        task.AiPriorityScore = ComputeHeuristicScore(task);

        await _database.SaveTaskAsync(task);
        _logger.LogInformation("Created task {Id}: {Title}", task.Id, task.Title);

        return task;
    }

    public async Task<TaskDto> UpdateTaskAsync(TaskUpdateDto dto, CancellationToken ct)
    {
        var task = await _database.GetTaskAsync(dto.Id.ToString());
        if (task == null)
        {
            throw new InvalidOperationException($"Task {dto.Id} not found");
        }

        // Apply updates
        if (dto.Title != null) task.Title = dto.Title;
        if (dto.Description != null) task.Description = dto.Description;
        if (dto.Priority.HasValue) task.Importance = dto.Priority.Value;
        if (dto.Status.HasValue)
        {
            task.Status = dto.Status.Value;
            if (dto.Status.Value == Shared.Models.TaskStatus.Completed && !task.CompletedAt.HasValue)
            {
                task.CompletedAt = DateTime.UtcNow;
            }
        }
        if (dto.EstimatedHours.HasValue) task.EstimatedMinutes = dto.EstimatedHours.Value * 60;
        if (dto.DeadlineDate.HasValue) task.DueDate = dto.DeadlineDate.Value;

        task.UpdatedAt = DateTime.UtcNow;
        task.AiPriorityScore = ComputeHeuristicScore(task);

        await _database.SaveTaskAsync(task);
        _logger.LogInformation("Updated task {Id}", task.Id);

        return task;
    }

    public async Task DeleteTaskAsync(Guid taskId, CancellationToken ct)
    {
        await _database.DeleteTaskAsync(taskId.ToString());
        _logger.LogInformation("Deleted task {Id}", taskId);
    }

    public async Task<IReadOnlyList<TaskDto>> GetTasksAsync(Guid? projectId, CancellationToken ct)
    {
        var query = new TaskQuery { ProjectId = projectId?.ToString() };
        var tasks = await _database.GetTasksAsync(query);
        return tasks;
    }

    public async Task<TaskDto?> GetTaskAsync(Guid taskId, CancellationToken ct)
    {
        return await _database.GetTaskAsync(taskId.ToString());
    }

    public async Task AddDependencyAsync(Guid taskId, Guid dependsOnId, CancellationToken ct)
    {
        var task = await _database.GetTaskAsync(taskId.ToString());
        if (task == null) return;

        if (!task.Dependencies.Contains(dependsOnId.ToString()))
        {
            task.Dependencies.Add(dependsOnId.ToString());
            task.UpdatedAt = DateTime.UtcNow;
            await _database.SaveTaskAsync(task);
            _logger.LogInformation("Added dependency {DependsOn} to task {TaskId}", dependsOnId, taskId);
        }
    }

    public async Task RemoveDependencyAsync(Guid taskId, Guid dependsOnId, CancellationToken ct)
    {
        var task = await _database.GetTaskAsync(taskId.ToString());
        if (task == null) return;

        if (task.Dependencies.Remove(dependsOnId.ToString()))
        {
            task.UpdatedAt = DateTime.UtcNow;
            await _database.SaveTaskAsync(task);
            _logger.LogInformation("Removed dependency {DependsOn} from task {TaskId}", dependsOnId, taskId);
        }
    }

    public async Task<IEnumerable<TaskDto>> ListTasksAsync(TaskQuery query, CancellationToken ct)
    {
        return await _database.GetTasksAsync(query);
    }

    #endregion

    #region Priority Scoring

    public async Task<TaskPriorityResult[]> ComputePrioritiesAsync(string[] taskIds, CancellationToken ct)
    {
        var results = new List<TaskPriorityResult>();
        var tasks = new List<TaskDto>();

        foreach (var taskId in taskIds)
        {
            var task = await _database.GetTaskAsync(taskId);
            if (task != null)
            {
                tasks.Add(task);
            }
        }

        // Compute heuristic scores
        foreach (var task in tasks)
        {
            var heuristic = ComputeHeuristicScore(task);
            results.Add(new TaskPriorityResult
            {
                TaskId = task.Id,
                HeuristicScore = heuristic,
                FinalScore = heuristic,
                Rationale = GenerateHeuristicRationale(task, heuristic)
            });
        }

        // Attempt AI augmentation if available
        try
        {
            var aiAdjustments = await GetAiPriorityAdjustmentsAsync(tasks, ct);
            if (aiAdjustments != null && aiAdjustments.Count > 0)
            {
                foreach (var result in results)
                {
                    if (aiAdjustments.TryGetValue(result.TaskId, out var aiScore))
                    {
                        result.AiScore = aiScore.Score;
                        result.FinalScore = 0.7 * result.HeuristicScore + 0.3 * aiScore.Score;
                        result.Rationale = aiScore.Rationale ?? result.Rationale;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI priority augmentation failed, using heuristic only");
        }

        // Update tasks with new scores
        foreach (var result in results)
        {
            var task = tasks.FirstOrDefault(t => t.Id == result.TaskId);
            if (task != null)
            {
                task.AiPriorityScore = result.FinalScore;
                task.PriorityRationale = result.Rationale;
                await _database.SaveTaskAsync(task);
            }
        }

        return results.OrderByDescending(r => r.FinalScore).ToArray();
    }

    private double ComputeHeuristicScore(TaskDto task)
    {
        // Importance score (0-1)
        var importanceScore = (int)task.Importance / 4.0;

        // Deadline urgency score (0-1)
        var deadlineScore = 0.0;
        if (task.DueDate.HasValue)
        {
            var daysUntilDue = (task.DueDate.Value - DateTime.UtcNow).TotalDays;
            if (daysUntilDue <= 0) deadlineScore = 1.0; // Overdue
            else if (daysUntilDue <= 1) deadlineScore = 0.9;
            else if (daysUntilDue <= 3) deadlineScore = 0.7;
            else if (daysUntilDue <= 7) deadlineScore = 0.5;
            else if (daysUntilDue <= 14) deadlineScore = 0.3;
            else deadlineScore = 0.1;
        }

        // Difficulty inverse score (easier = higher priority for quick wins)
        var difficultyInverse = 1.0 - ((int)task.Difficulty / 5.0);

        // Size inverse score (smaller = higher priority)
        var sizeInverse = 1.0 - Math.Min(1.0, task.EstimatedMinutes / 480.0); // 8 hours max

        // Risk boost (dependencies, blocked status)
        var riskBoost = 0.0;
        if (task.Dependencies.Count > 0) riskBoost += 0.2;
        if (task.Status == Shared.Models.TaskStatus.Blocked) riskBoost += 0.3;

        var score = 
            ImportanceWeight * importanceScore +
            DeadlineWeight * deadlineScore +
            DifficultyInverseWeight * difficultyInverse +
            SizeInverseWeight * sizeInverse +
            RiskBoostWeight * riskBoost;

        return Math.Min(1.0, Math.Max(0.0, score));
    }

    private string GenerateHeuristicRationale(TaskDto task, double score)
    {
        var reasons = new List<string>();

        if (task.Importance >= TaskPriority.High)
            reasons.Add("High importance");
        
        if (task.DueDate.HasValue)
        {
            var daysUntilDue = (task.DueDate.Value - DateTime.UtcNow).TotalDays;
            if (daysUntilDue <= 0) reasons.Add("Overdue!");
            else if (daysUntilDue <= 3) reasons.Add("Due soon");
        }

        if (task.Difficulty <= TaskDifficulty.Easy)
            reasons.Add("Quick win opportunity");

        if (task.Dependencies.Count > 0)
            reasons.Add("Has dependencies");

        if (reasons.Count == 0)
            reasons.Add("Standard priority");

        return string.Join("; ", reasons);
    }

    private async Task<Dictionary<string, (double Score, string? Rationale)>?> GetAiPriorityAdjustmentsAsync(
        List<TaskDto> tasks, CancellationToken ct)
    {
        // Use LLM to get priority adjustments
        var prompt = BuildPriorityPrompt(tasks);
        var response = await _llmService.GetCompletionAsync(prompt, ct);
        
        if (string.IsNullOrEmpty(response))
            return null;

        // Parse AI response (expects JSON-like structured output)
        return ParseAiPriorityResponse(response, tasks);
    }

    private string BuildPriorityPrompt(List<TaskDto> tasks)
    {
        var taskList = string.Join("\n", tasks.Select(t => 
            $"- [{t.Id}] {t.Title}: importance={t.Importance}, difficulty={t.Difficulty}, due={t.DueDate?.ToString("d") ?? "none"}, est={t.EstimatedMinutes}min"));

        return $@"Analyze these tasks and suggest priority adjustments (0.0-1.0 scale):

{taskList}

For each task, respond with:
TASK_ID: score (rationale)

Consider: urgency, impact, dependencies, effort-to-value ratio.";
    }

    private Dictionary<string, (double Score, string? Rationale)>? ParseAiPriorityResponse(string response, List<TaskDto> tasks)
    {
        var result = new Dictionary<string, (double, string?)>();

        foreach (var task in tasks)
        {
            // Simple parsing - look for task ID and score
            var pattern = $@"{task.Id}[:\s]+([0-9.]+)\s*\(?([^)]*)\)?";
            var match = System.Text.RegularExpressions.Regex.Match(response, pattern);
            
            if (match.Success && double.TryParse(match.Groups[1].Value, out var score))
            {
                result[task.Id] = (Math.Min(1.0, Math.Max(0.0, score)), match.Groups[2].Value);
            }
        }

        return result.Count > 0 ? result : null;
    }

    #endregion
}
