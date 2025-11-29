using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using FlowVault.Shared.Contracts;
using FlowVault.Shared.Models;
using FlowVault.BackendHost.Services;
using FlowVault.BackendHost.Persistence;

namespace FlowVault.BackendHost.IPC;

/// <summary>
/// Routes IPC requests to appropriate services
/// </summary>
public class BackendApiHandler
{
    private readonly ILogger<BackendApiHandler> _logger;
    private readonly DatabaseService _db;
    private readonly IndexerService _indexer;
    private readonly TaskService _taskService;
    private readonly SchedulerService _scheduler;
    private readonly GraphService _graph;
    private readonly LlmService _llm;
    private readonly ApiKeyService _apiKeys;

    public BackendApiHandler(
        ILogger<BackendApiHandler> logger,
        DatabaseService db,
        IndexerService indexer,
        TaskService taskService,
        SchedulerService scheduler,
        GraphService graph,
        LlmService llm,
        ApiKeyService apiKeys)
    {
        _logger = logger;
        _db = db;
        _indexer = indexer;
        _taskService = taskService;
        _scheduler = scheduler;
        _graph = graph;
        _llm = llm;
        _apiKeys = apiKeys;
    }

    public async Task<IpcResponse> HandleRequestAsync(IpcMessage message, CancellationToken ct)
    {
        try
        {
            object? result = message.Method switch
            {
                // Projects
                "projects.list" => await GetProjectsAsync(ct),
                "projects.get" => await GetProjectAsync(GetParam<Guid>(message, "projectId"), ct),
                "projects.create" => await CreateProjectAsync(GetParam<ProjectDto>(message, "project"), ct),
                "projects.update" => await UpdateProjectAsync(GetParam<ProjectDto>(message, "project"), ct),
                "projects.delete" => await DeleteProjectAsync(GetParam<Guid>(message, "projectId"), ct),
                "projects.setActive" => await SetActiveProjectAsync(GetParam<Guid>(message, "projectId"), ct),

                // Folders
                "folders.list" => await GetFolderSummariesAsync(GetParam<Guid>(message, "projectId"), ct),
                "folders.get" => await GetFolderSummaryAsync(GetParam<string>(message, "path"), ct),

                // Files
                "files.listByFolder" => await GetFileSummariesAsync(GetParam<string>(message, "folderPath"), ct),
                "files.get" => await GetFileSummaryAsync(GetParam<string>(message, "filePath"), ct),
                "files.search" => await SearchFilesAsync(GetParam<string>(message, "query"), GetParam<Guid?>(message, "projectId"), ct),

                // Tasks
                "tasks.list" => await GetTasksAsync(GetParam<Guid?>(message, "projectId"), ct),
                "tasks.get" => await GetTaskAsync(GetParam<Guid>(message, "taskId"), ct),
                "tasks.create" => await CreateTaskAsync(GetParam<TaskCreateDto>(message, "task"), ct),
                "tasks.update" => await UpdateTaskAsync(GetParam<TaskUpdateDto>(message, "task"), ct),
                "tasks.delete" => await DeleteTaskAsync(GetParam<Guid>(message, "taskId"), ct),
                "tasks.dependencies.add" => await AddDependencyAsync(GetParam<Guid>(message, "taskId"), GetParam<Guid>(message, "dependsOnId"), ct),
                "tasks.dependencies.remove" => await RemoveDependencyAsync(GetParam<Guid>(message, "taskId"), GetParam<Guid>(message, "dependsOnId"), ct),

                // Schedule
                "schedule.get" => await GetScheduleAsync(GetParam<DateTime>(message, "weekStart"), ct),
                "schedule.autoSchedule" => await AutoScheduleAsync(GetParam<Guid?>(message, "projectId"), ct),
                "schedule.updateBlock" => await UpdateScheduleBlockAsync(GetParam<ScheduleBlockDto>(message, "block"), ct),
                "schedule.resolveConflicts" => await ResolveConflictsAsync(ct),

                // Calendar
                "calendar.events" => await GetCalendarEventsAsync(GetParam<DateTime>(message, "start"), GetParam<DateTime>(message, "end"), ct),
                "calendar.addEvent" => await AddCalendarEventAsync(GetParam<CalendarEventDto>(message, "event"), ct),
                "calendar.updateEvent" => await UpdateCalendarEventAsync(GetParam<CalendarEventDto>(message, "event"), ct),
                "calendar.deleteEvent" => await DeleteCalendarEventAsync(GetParam<Guid>(message, "eventId"), ct),

                // Graph
                "graph.generate" => await GenerateGraphAsync(GetParam<Guid?>(message, "projectId"), ct),

                // Pins
                "pins.list" => await GetPinnedTilesAsync(ct),
                "pins.save" => await SavePinnedTileAsync(GetParam<PinnedTileDto>(message, "tile"), ct),
                "pins.delete" => await DeletePinnedTileAsync(GetParam<Guid>(message, "tileId"), ct),

                // API Keys
                "apiKeys.get" => await GetApiKeyAsync(GetParam<LlmProvider>(message, "provider"), ct),
                "apiKeys.save" => await SaveApiKeyAsync(GetParam<LlmProvider>(message, "provider"), GetParam<string>(message, "key"), ct),
                "apiKeys.delete" => await DeleteApiKeyAsync(GetParam<LlmProvider>(message, "provider"), ct),
                "apiKeys.test" => await TestApiKeyAsync(GetParam<LlmProvider>(message, "provider"), GetParam<string>(message, "key"), ct),

                // Indexer
                "indexer.reindex" => await ReindexAsync(GetParam<Guid>(message, "projectId"), ct),
                "indexer.status" => await GetIndexerStatusAsync(ct),

                _ => throw new NotSupportedException($"Unknown method: {message.Method}")
            };

            return new IpcResponse
            {
                RequestId = message.RequestId,
                Success = true,
                Payload = result != null ? JsonSerializer.SerializeToElement(result) : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request: {Method}", message.Method);
            return new IpcResponse
            {
                RequestId = message.RequestId,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async IAsyncEnumerable<IpcResponse> HandleStreamingRequestAsync(
        IpcMessage message, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (message.Method == "chat.stream")
        {
            var chatRequest = GetParam<ChatRequestDto>(message, "request");
            await foreach (var chunk in ChatStreamAsync(chatRequest, ct))
            {
                yield return new IpcResponse
                {
                    RequestId = message.RequestId,
                    Success = true,
                    IsStreaming = true,
                    StreamComplete = chunk.IsComplete,
                    Payload = JsonSerializer.SerializeToElement(chunk)
                };
            }
        }
        else
        {
            // Non-streaming method - return single response
            var response = await HandleRequestAsync(message, ct);
            yield return response;
        }
    }

    private T GetParam<T>(IpcMessage message, string key)
    {
        if (message.Parameters == null || !message.Parameters.TryGetValue(key, out var element))
        {
            if (typeof(T).IsClass || Nullable.GetUnderlyingType(typeof(T)) != null)
                return default!;
            throw new ArgumentException($"Missing required parameter: {key}");
        }

        return JsonSerializer.Deserialize<T>(element.GetRawText()) 
            ?? throw new ArgumentException($"Invalid value for parameter: {key}");
    }

    // === Internal Handler Methods ===

    private async Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(CancellationToken ct)
    {
        return await _db.GetProjectsAsync();
    }

    private async Task<ProjectDto?> GetProjectAsync(Guid projectId, CancellationToken ct)
    {
        return await _db.GetProjectAsync(projectId);
    }

    private async Task<ProjectDto> CreateProjectAsync(ProjectDto project, CancellationToken ct)
    {
        project.Id = Guid.NewGuid();
        project.CreatedAt = DateTime.UtcNow;
        await _db.SaveProjectAsync(project);
        return project;
    }

    private async Task<ProjectDto> UpdateProjectAsync(ProjectDto project, CancellationToken ct)
    {
        await _db.SaveProjectAsync(project);
        return project;
    }

    private async Task<bool> DeleteProjectAsync(Guid projectId, CancellationToken ct)
    {
        await _db.DeleteProjectAsync(projectId);
        return true;
    }

    private async Task<bool> SetActiveProjectAsync(Guid projectId, CancellationToken ct)
    {
        // Deactivate all, then activate selected
        var projects = await _db.GetProjectsAsync();
        foreach (var p in projects)
        {
            p.IsActive = p.Id == projectId;
            await _db.SaveProjectAsync(p);
        }
        return true;
    }

    private async Task<IReadOnlyList<FolderSummaryDto>> GetFolderSummariesAsync(Guid projectId, CancellationToken ct)
    {
        return await _db.GetFolderSummariesAsync(projectId);
    }

    private async Task<FolderSummaryDto?> GetFolderSummaryAsync(string path, CancellationToken ct)
    {
        return await _db.GetFolderSummaryAsync(path);
    }

    private async Task<IReadOnlyList<FileSummaryDto>> GetFileSummariesAsync(string folderPath, CancellationToken ct)
    {
        return await _db.GetFileSummariesByFolderAsync(folderPath);
    }

    private async Task<FileSummaryDto?> GetFileSummaryAsync(string filePath, CancellationToken ct)
    {
        return await _db.GetFileSummaryAsync(filePath);
    }

    private async Task<IReadOnlyList<FileSummaryDto>> SearchFilesAsync(string query, Guid? projectId, CancellationToken ct)
    {
        return await _db.SearchFilesAsync(query, projectId);
    }

    private async Task<IReadOnlyList<TaskDto>> GetTasksAsync(Guid? projectId, CancellationToken ct)
    {
        return await _taskService.GetTasksAsync(projectId, ct);
    }

    private async Task<TaskDto?> GetTaskAsync(Guid taskId, CancellationToken ct)
    {
        return await _taskService.GetTaskAsync(taskId, ct);
    }

    private async Task<TaskDto> CreateTaskAsync(TaskCreateDto dto, CancellationToken ct)
    {
        return await _taskService.CreateTaskAsync(dto, ct);
    }

    private async Task<TaskDto> UpdateTaskAsync(TaskUpdateDto dto, CancellationToken ct)
    {
        return await _taskService.UpdateTaskAsync(dto, ct);
    }

    private async Task<bool> DeleteTaskAsync(Guid taskId, CancellationToken ct)
    {
        await _taskService.DeleteTaskAsync(taskId, ct);
        return true;
    }

    private async Task<bool> AddDependencyAsync(Guid taskId, Guid dependsOnId, CancellationToken ct)
    {
        await _taskService.AddDependencyAsync(taskId, dependsOnId, ct);
        return true;
    }

    private async Task<bool> RemoveDependencyAsync(Guid taskId, Guid dependsOnId, CancellationToken ct)
    {
        await _taskService.RemoveDependencyAsync(taskId, dependsOnId, ct);
        return true;
    }

    private async Task<ScheduleDto> GetScheduleAsync(DateTime weekStart, CancellationToken ct)
    {
        return await _scheduler.GetScheduleAsync(weekStart, ct);
    }

    private async Task<ScheduleDto> AutoScheduleAsync(Guid? projectId, CancellationToken ct)
    {
        return await _scheduler.AutoScheduleAsync(projectId, ct);
    }

    private async Task<bool> UpdateScheduleBlockAsync(ScheduleBlockDto block, CancellationToken ct)
    {
        await _scheduler.UpdateBlockAsync(block, ct);
        return true;
    }

    private async Task<IReadOnlyList<ScheduleConflictDto>> ResolveConflictsAsync(CancellationToken ct)
    {
        return await _scheduler.ResolveConflictsAsync(ct);
    }

    private async Task<IReadOnlyList<CalendarEventDto>> GetCalendarEventsAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        return await _db.GetCalendarEventsAsync(start, end);
    }

    private async Task<CalendarEventDto> AddCalendarEventAsync(CalendarEventDto evt, CancellationToken ct)
    {
        evt.Id = Guid.NewGuid();
        await _db.SaveCalendarEventAsync(evt);
        return evt;
    }

    private async Task<CalendarEventDto> UpdateCalendarEventAsync(CalendarEventDto evt, CancellationToken ct)
    {
        await _db.SaveCalendarEventAsync(evt);
        return evt;
    }

    private async Task<bool> DeleteCalendarEventAsync(Guid eventId, CancellationToken ct)
    {
        await _db.DeleteCalendarEventAsync(eventId);
        return true;
    }

    private async Task<WorkflowGraphDto> GenerateGraphAsync(Guid? projectId, CancellationToken ct)
    {
        return await _graph.GenerateGraphAsync(projectId, ct);
    }

    private async Task<IReadOnlyList<PinnedTileDto>> GetPinnedTilesAsync(CancellationToken ct)
    {
        return await _db.GetPinnedTilesAsync();
    }

    private async Task<bool> SavePinnedTileAsync(PinnedTileDto tile, CancellationToken ct)
    {
        if (tile.Id == Guid.Empty)
            tile.Id = Guid.NewGuid();
        await _db.SavePinnedTileAsync(tile);
        return true;
    }

    private async Task<bool> DeletePinnedTileAsync(Guid tileId, CancellationToken ct)
    {
        await _db.DeletePinnedTileAsync(tileId);
        return true;
    }

    private async Task<string?> GetApiKeyAsync(LlmProvider provider, CancellationToken ct)
    {
        return await _apiKeys.GetKeyAsync(provider, ct);
    }

    private async Task<bool> SaveApiKeyAsync(LlmProvider provider, string key, CancellationToken ct)
    {
        await _apiKeys.SaveKeyAsync(provider, key, ct);
        return true;
    }

    private async Task<bool> DeleteApiKeyAsync(LlmProvider provider, CancellationToken ct)
    {
        await _apiKeys.DeleteKeyAsync(provider, ct);
        return true;
    }

    private async Task<bool> TestApiKeyAsync(LlmProvider provider, string key, CancellationToken ct)
    {
        return await _apiKeys.TestKeyAsync(provider, key, ct);
    }

    private async IAsyncEnumerable<ChatChunkDto> ChatStreamAsync(
        ChatRequestDto request, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var chunk in _llm.StreamResponseAsync(request, ct))
        {
            yield return chunk;
        }
    }

    private async Task<bool> ReindexAsync(Guid projectId, CancellationToken ct)
    {
        var project = await _db.GetProjectAsync(projectId);
        if (project != null)
        {
            await _indexer.ReindexProjectAsync(project.RootPath, projectId, ct);
        }
        return true;
    }

    private async Task<IndexerStatusDto> GetIndexerStatusAsync(CancellationToken ct)
    {
        return await Task.FromResult(new IndexerStatusDto
        {
            IsRunning = true,
            FilesIndexed = 0,
            LastIndexTime = DateTime.UtcNow
        });
    }
}

public class IndexerStatusDto
{
    public bool IsRunning { get; set; }
    public int FilesIndexed { get; set; }
    public DateTime LastIndexTime { get; set; }
}
