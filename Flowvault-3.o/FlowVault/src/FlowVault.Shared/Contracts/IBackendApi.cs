using FlowVault.Shared.Models;

namespace FlowVault.Shared.Contracts;

/// <summary>
/// Backend API contract for IPC communication between UI and BackendHost
/// </summary>
public interface IBackendApi
{
    #region Chat Streaming
    
    /// <summary>
    /// Stream chat responses token by token
    /// </summary>
    IAsyncEnumerable<StreamToken> StreamChatResponses(ChatRequest request, CancellationToken ct = default);
    
    #endregion

    #region File & Folder Summaries
    
    /// <summary>
    /// Get summary for a specific file
    /// </summary>
    Task<FileSummaryDto> GetFileSummary(string filePath, CancellationToken ct = default);
    
    /// <summary>
    /// Get summary for a folder
    /// </summary>
    Task<FolderSummaryDto> GetFolderSummary(string folderPath, CancellationToken ct = default);
    
    #endregion

    #region Tasks
    
    /// <summary>
    /// Create a new task
    /// </summary>
    Task<TaskDto> CreateTask(TaskCreateDto dto, CancellationToken ct = default);
    
    /// <summary>
    /// Update an existing task
    /// </summary>
    Task<TaskDto> UpdateTask(TaskDto dto, CancellationToken ct = default);
    
    /// <summary>
    /// Delete a task
    /// </summary>
    Task DeleteTask(string taskId, CancellationToken ct = default);
    
    /// <summary>
    /// List tasks with optional filtering
    /// </summary>
    Task<IEnumerable<TaskDto>> ListTasks(TaskQuery query, CancellationToken ct = default);
    
    /// <summary>
    /// Compute priority scores for tasks
    /// </summary>
    Task<TaskPriorityResult[]> ComputePriorities(string[] taskIds, CancellationToken ct = default);
    
    #endregion

    #region Scheduler
    
    /// <summary>
    /// Auto-schedule tasks into calendar blocks
    /// </summary>
    Task<SchedulingResult> AutoSchedule(ScheduleRequest request, CancellationToken ct = default);
    
    /// <summary>
    /// Get calendar blocks for a date range
    /// </summary>
    Task<IEnumerable<CalendarBlockDto>> GetCalendarBlocks(DateTime start, DateTime end, CancellationToken ct = default);
    
    /// <summary>
    /// Accept or reject a proposed schedule block
    /// </summary>
    Task<CalendarBlockDto> UpdateBlockStatus(string blockId, ScheduleBlockStatus status, CancellationToken ct = default);
    
    #endregion

    #region Indexer
    
    /// <summary>
    /// Start indexing a project
    /// </summary>
    Task StartIndexing(ProjectConfig project, CancellationToken ct = default);
    
    /// <summary>
    /// Stop indexing
    /// </summary>
    Task StopIndexing(string projectId, CancellationToken ct = default);
    
    /// <summary>
    /// Get current index status
    /// </summary>
    Task<IndexStatus> GetIndexStatus(string projectId, CancellationToken ct = default);
    
    #endregion

    #region Workflow Graph
    
    /// <summary>
    /// Generate workflow graph from tasks
    /// </summary>
    Task<WorkflowGraphDto> GenerateWorkflowGraph(string[] taskIds, CancellationToken ct = default);
    
    #endregion

    #region Key Management
    
    /// <summary>
    /// Test an API key connection
    /// </summary>
    Task<ApiKeyTestResult> TestApiKey(ApiKeyDto apiKey, CancellationToken ct = default);
    
    /// <summary>
    /// Save an API key (encrypted)
    /// </summary>
    Task SaveApiKey(ApiKeyDto apiKey, CancellationToken ct = default);
    
    /// <summary>
    /// Get all API keys (masked)
    /// </summary>
    Task<IEnumerable<ApiKeyDto>> GetApiKeys(CancellationToken ct = default);
    
    /// <summary>
    /// Delete an API key
    /// </summary>
    Task DeleteApiKey(string keyId, CancellationToken ct = default);
    
    #endregion

    #region Pinned Tiles
    
    /// <summary>
    /// Get all pinned tiles
    /// </summary>
    Task<IEnumerable<PinnedTileDto>> GetPinnedTiles(CancellationToken ct = default);
    
    /// <summary>
    /// Save a pinned tile
    /// </summary>
    Task SavePinnedTile(PinnedTileDto tile, CancellationToken ct = default);
    
    /// <summary>
    /// Delete a pinned tile
    /// </summary>
    Task DeletePinnedTile(string tileId, CancellationToken ct = default);
    
    #endregion

    #region Projects
    
    /// <summary>
    /// Get all projects
    /// </summary>
    Task<IEnumerable<ProjectConfig>> GetProjects(CancellationToken ct = default);
    
    /// <summary>
    /// Add a project
    /// </summary>
    Task<ProjectConfig> AddProject(ProjectConfig project, CancellationToken ct = default);
    
    /// <summary>
    /// Remove a project
    /// </summary>
    Task RemoveProject(string projectId, CancellationToken ct = default);
    
    #endregion
}
