using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FlowVault.Shared.Contracts;
using FlowVault.Shared.Models;

namespace FlowVault.UI.Services;

/// <summary>
/// IPC client for communicating with BackendHost
/// </summary>
public class BackendClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isConnected;

    public bool IsConnected => _isConnected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _pipe = new NamedPipeClientStream(".", IpcConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        
        try
        {
            await _pipe.ConnectAsync(5000, ct);
            _reader = new StreamReader(_pipe, Encoding.UTF8, leaveOpen: true);
            _writer = new StreamWriter(_pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            _isConnected = true;
        }
        catch
        {
            _isConnected = false;
            throw;
        }
    }

    public async Task<T?> SendRequestAsync<T>(string method, Dictionary<string, object>? parameters = null, CancellationToken ct = default)
    {
        if (!_isConnected || _writer == null || _reader == null)
            throw new InvalidOperationException("Not connected to backend");

        await _lock.WaitAsync(ct);
        try
        {
            var request = new IpcMessage
            {
                RequestId = Guid.NewGuid().ToString(),
                Method = method,
                Parameters = parameters?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => JsonSerializer.SerializeToElement(kvp.Value))
            };

            var json = JsonSerializer.Serialize(request);
            await _writer.WriteLineAsync(json);

            var responseLine = await _reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(responseLine))
                throw new InvalidOperationException("Empty response from backend");

            var response = JsonSerializer.Deserialize<IpcResponse>(responseLine);
            if (response == null)
                throw new InvalidOperationException("Invalid response from backend");

            if (!response.Success)
                throw new InvalidOperationException(response.ErrorMessage ?? "Backend error");

            if (response.Payload == null)
                return default;

            return JsonSerializer.Deserialize<T>(response.Payload.Value.GetRawText());
        }
        finally
        {
            _lock.Release();
        }
    }

    public async IAsyncEnumerable<ChatChunkDto> StreamChatAsync(
        ChatRequestDto request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_isConnected || _writer == null || _reader == null)
            throw new InvalidOperationException("Not connected to backend");

        await _lock.WaitAsync(ct);
        try
        {
            var ipcMessage = new IpcMessage
            {
                RequestId = Guid.NewGuid().ToString(),
                Method = "chat.stream",
                IsStreaming = true,
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["request"] = JsonSerializer.SerializeToElement(request)
                }
            };

            var json = JsonSerializer.Serialize(ipcMessage);
            await _writer.WriteLineAsync(json);

            while (!ct.IsCancellationRequested)
            {
                var responseLine = await _reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(responseLine))
                    break;

                var response = JsonSerializer.Deserialize<IpcResponse>(responseLine);
                if (response == null)
                    break;

                if (!response.Success)
                    throw new InvalidOperationException(response.ErrorMessage ?? "Backend error");

                if (response.Payload != null)
                {
                    var chunk = JsonSerializer.Deserialize<ChatChunkDto>(response.Payload.Value.GetRawText());
                    if (chunk != null)
                        yield return chunk;
                }

                if (response.StreamComplete)
                    break;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    // === Convenience Methods ===

    public Task<IReadOnlyList<ProjectDto>?> GetProjectsAsync(CancellationToken ct = default)
        => SendRequestAsync<IReadOnlyList<ProjectDto>>("projects.list", null, ct);

    public Task<ProjectDto?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
        => SendRequestAsync<ProjectDto>("projects.get", new() { ["projectId"] = projectId }, ct);

    public Task<ProjectDto?> CreateProjectAsync(ProjectDto project, CancellationToken ct = default)
        => SendRequestAsync<ProjectDto>("projects.create", new() { ["project"] = project }, ct);

    public Task<IReadOnlyList<FolderSummaryDto>?> GetFolderSummariesAsync(Guid projectId, CancellationToken ct = default)
        => SendRequestAsync<IReadOnlyList<FolderSummaryDto>>("folders.list", new() { ["projectId"] = projectId }, ct);

    public Task<IReadOnlyList<FileSummaryDto>?> GetFileSummariesAsync(string folderPath, CancellationToken ct = default)
        => SendRequestAsync<IReadOnlyList<FileSummaryDto>>("files.listByFolder", new() { ["folderPath"] = folderPath }, ct);

    public Task<IReadOnlyList<TaskDto>?> GetTasksAsync(Guid? projectId = null, CancellationToken ct = default)
        => SendRequestAsync<IReadOnlyList<TaskDto>>("tasks.list", new() { ["projectId"] = projectId! }, ct);

    public Task<TaskDto?> CreateTaskAsync(TaskCreateDto task, CancellationToken ct = default)
        => SendRequestAsync<TaskDto>("tasks.create", new() { ["task"] = task }, ct);

    public Task<TaskDto?> UpdateTaskAsync(TaskUpdateDto task, CancellationToken ct = default)
        => SendRequestAsync<TaskDto>("tasks.update", new() { ["task"] = task }, ct);

    public Task<ScheduleDto?> GetScheduleAsync(DateTime weekStart, CancellationToken ct = default)
        => SendRequestAsync<ScheduleDto>("schedule.get", new() { ["weekStart"] = weekStart }, ct);

    public Task<ScheduleDto?> AutoScheduleAsync(Guid? projectId = null, CancellationToken ct = default)
        => SendRequestAsync<ScheduleDto>("schedule.autoSchedule", new() { ["projectId"] = projectId! }, ct);

    public Task<IReadOnlyList<CalendarEventDto>?> GetCalendarEventsAsync(DateTime start, DateTime end, CancellationToken ct = default)
        => SendRequestAsync<IReadOnlyList<CalendarEventDto>>("calendar.events", new() { ["start"] = start, ["end"] = end }, ct);

    public Task<WorkflowGraphDto?> GenerateGraphAsync(Guid? projectId = null, CancellationToken ct = default)
        => SendRequestAsync<WorkflowGraphDto>("graph.generate", new() { ["projectId"] = projectId! }, ct);

    public Task<IReadOnlyList<PinnedTileDto>?> GetPinnedTilesAsync(CancellationToken ct = default)
        => SendRequestAsync<IReadOnlyList<PinnedTileDto>>("pins.list", null, ct);

    public Task SavePinnedTileAsync(PinnedTileDto tile, CancellationToken ct = default)
        => SendRequestAsync<object>("pins.save", new() { ["tile"] = tile }, ct);

    public Task DeletePinnedTileAsync(Guid tileId, CancellationToken ct = default)
        => SendRequestAsync<object>("pins.delete", new() { ["tileId"] = tileId }, ct);

    public Task<string?> GetApiKeyAsync(LlmProvider provider, CancellationToken ct = default)
        => SendRequestAsync<string>("apiKeys.get", new() { ["provider"] = provider }, ct);

    public Task SaveApiKeyAsync(LlmProvider provider, string key, CancellationToken ct = default)
        => SendRequestAsync<object>("apiKeys.save", new() { ["provider"] = provider, ["key"] = key }, ct);

    public Task<bool> TestApiKeyAsync(LlmProvider provider, string key, CancellationToken ct = default)
        => SendRequestAsync<bool>("apiKeys.test", new() { ["provider"] = provider, ["key"] = key }, ct)!;

    public void Dispose()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _pipe?.Dispose();
        _lock.Dispose();
    }
}
