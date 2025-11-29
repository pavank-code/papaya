using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FlowVault.Shared.Contracts;

namespace FlowVault.BackendHost.IPC;

/// <summary>
/// Named Pipe IPC server for communication with UI
/// </summary>
public class IpcServer : BackgroundService
{
    private readonly ILogger<IpcServer> _logger;
    private readonly BackendApiHandler _handler;
    
    public IpcServer(ILogger<IpcServer> logger, BackendApiHandler handler)
    {
        _logger = logger;
        _handler = handler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IPC Server starting on pipe: {PipeName}", IpcConstants.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipeServer = new NamedPipeServerStream(
                    IpcConstants.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _logger.LogDebug("Waiting for client connection...");
                await pipeServer.WaitForConnectionAsync(stoppingToken);
                _logger.LogDebug("Client connected");

                // Handle connection in separate task
                _ = HandleConnectionAsync(pipeServer, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in IPC server");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("IPC Server stopped");
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true);
            writer.AutoFlush = true;
            await using var _ = writer;

            while (pipe.IsConnected && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var message = JsonSerializer.Deserialize<IpcMessage>(line);
                    if (message == null) continue;

                    _logger.LogDebug("Received IPC request: {Method}", message.Method);

                    if (message.IsStreaming)
                    {
                        // Handle streaming responses
                        await foreach (var response in _handler.HandleStreamingRequestAsync(message, ct))
                        {
                            var json = JsonSerializer.Serialize(response);
                            await writer.WriteLineAsync(json);
                        }
                    }
                    else
                    {
                        // Handle single response
                        var response = await _handler.HandleRequestAsync(message, ct);
                        var json = JsonSerializer.Serialize(response);
                        await writer.WriteLineAsync(json);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid JSON in IPC message");
                    await SendErrorAsync(writer, "invalid_json", "Invalid JSON format");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling IPC request");
                    await SendErrorAsync(writer, "error", ex.Message);
                }
            }
        }
        catch (IOException)
        {
            // Client disconnected
            _logger.LogDebug("Client disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in connection handler");
        }
    }

    private async Task SendErrorAsync(StreamWriter writer, string requestId, string message)
    {
        var response = new IpcResponse
        {
            RequestId = requestId,
            Success = false,
            ErrorMessage = message
        };
        var json = JsonSerializer.Serialize(response);
        await writer.WriteLineAsync(json);
    }
}
