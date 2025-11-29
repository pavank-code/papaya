using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowVault.Shared.Contracts;

/// <summary>
/// IPC message wrapper for Named Pipe communication
/// </summary>
public class IpcMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string Method { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public Dictionary<string, JsonElement>? Parameters { get; set; }
    public bool IsStreaming { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// IPC response wrapper
/// </summary>
public class IpcResponse
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public JsonElement? Payload { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsStreamChunk { get; set; }
    public bool IsStreamEnd { get; set; }
    public bool IsStreaming { get; set; }
    public bool StreamComplete { get; set; }
}

/// <summary>
/// Constants for IPC
/// </summary>
public static class IpcConstants
{
    public const string PipeName = "FlowVault_BackendPipe";
    public const int ConnectionTimeoutMs = 5000;
    public const int ReadTimeoutMs = 30000;
    
    // Method names
    public const string MethodStreamChat = "StreamChat";
    public const string MethodGetFileSummary = "GetFileSummary";
    public const string MethodGetFolderSummary = "GetFolderSummary";
    public const string MethodCreateTask = "CreateTask";
    public const string MethodUpdateTask = "UpdateTask";
    public const string MethodDeleteTask = "DeleteTask";
    public const string MethodListTasks = "ListTasks";
    public const string MethodComputePriorities = "ComputePriorities";
    public const string MethodAutoSchedule = "AutoSchedule";
    public const string MethodGetCalendarBlocks = "GetCalendarBlocks";
    public const string MethodUpdateBlockStatus = "UpdateBlockStatus";
    public const string MethodStartIndexing = "StartIndexing";
    public const string MethodStopIndexing = "StopIndexing";
    public const string MethodGetIndexStatus = "GetIndexStatus";
    public const string MethodGenerateWorkflow = "GenerateWorkflow";
    public const string MethodTestApiKey = "TestApiKey";
    public const string MethodSaveApiKey = "SaveApiKey";
    public const string MethodGetApiKeys = "GetApiKeys";
    public const string MethodDeleteApiKey = "DeleteApiKey";
    public const string MethodGetPinnedTiles = "GetPinnedTiles";
    public const string MethodSavePinnedTile = "SavePinnedTile";
    public const string MethodDeletePinnedTile = "DeletePinnedTile";
    public const string MethodGetProjects = "GetProjects";
    public const string MethodAddProject = "AddProject";
    public const string MethodRemoveProject = "RemoveProject";
}
