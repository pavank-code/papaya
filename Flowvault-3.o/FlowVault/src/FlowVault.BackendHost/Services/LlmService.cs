using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using FlowVault.Shared.Models;
using FlowVault.BackendHost.Persistence;

namespace FlowVault.BackendHost.Services;

/// <summary>
/// LLM service with streaming support and multiple provider adapters
/// </summary>
public class LlmService
{
    private readonly ILogger<LlmService> _logger;
    private readonly DatabaseService _database;
    private readonly MockLlmAdapter _mockAdapter;
    private readonly GeminiLlmAdapter _geminiAdapter;
    private readonly OpenAiLlmAdapter _openAiAdapter;

    private const string SystemPrompt = @"You are Flow Vault AI, an intelligent assistant for developers.
You help with:
- Task prioritization and planning
- Code understanding and file summaries
- Workflow organization
- Calendar scheduling

Guidelines:
- Be concise and actionable
- Provide rationales for recommendations
- When asked for structured data, respond with valid JSON
- Never modify code directly, only suggest changes";

    public LlmService(
        ILogger<LlmService> logger,
        DatabaseService database,
        MockLlmAdapter mockAdapter,
        GeminiLlmAdapter geminiAdapter,
        OpenAiLlmAdapter openAiAdapter)
    {
        _logger = logger;
        _database = database;
        _mockAdapter = mockAdapter;
        _geminiAdapter = geminiAdapter;
        _openAiAdapter = openAiAdapter;
    }

    /// <summary>
    /// Stream chat responses token by token
    /// </summary>
    public async IAsyncEnumerable<StreamToken> StreamChatResponsesAsync(
        ChatRequest request, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        var adapter = await GetAdapterAsync(request.PreferredProvider);
        var context = await BuildContextAsync(request);

        var tokenIndex = 0;
        var retryCount = 0;
        const int maxRetries = 3;

        while (retryCount < maxRetries)
        {
            bool success = true;
            
            await foreach (var token in adapter.StreamAsync(context, request.MaxTokens, request.Temperature, ct))
            {
                if (token.Meta.IsError)
                {
                    success = false;
                    retryCount++;
                    
                    if (retryCount < maxRetries)
                    {
                        _logger.LogWarning("LLM request failed, retrying ({Retry}/{Max})", retryCount, maxRetries);
                        await Task.Delay(GetBackoffDelay(retryCount), ct);
                        break;
                    }
                    
                    yield return new StreamToken
                    {
                        Token = $"[Error: {token.Meta.ErrorMessage}]",
                        IsFinal = true,
                        Meta = new TokenMetadata
                        {
                            IsError = true,
                            ErrorMessage = token.Meta.ErrorMessage,
                            Provider = adapter.Provider
                        }
                    };
                    yield break;
                }

                token.Meta.TokenIndex = tokenIndex++;
                token.Meta.Provider = adapter.Provider;
                yield return token;
            }

            if (success) break;
        }
    }

    /// <summary>
    /// Get a single completion (non-streaming)
    /// </summary>
    public async Task<string> GetCompletionAsync(string prompt, CancellationToken ct)
    {
        var request = new ChatRequest
        {
            Message = prompt,
            MaxTokens = 1024
        };

        var result = new System.Text.StringBuilder();

        await foreach (var token in StreamChatResponsesAsync(request, ct))
        {
            if (!token.Meta.IsError)
            {
                result.Append(token.Token);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Stream chat responses using ChatRequestDto (for IPC)
    /// </summary>
    public async IAsyncEnumerable<ChatChunkDto> StreamResponseAsync(
        ChatRequestDto request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var adapter = await GetAdapterAsync(request.Provider);
        
        // Build prompt from messages
        var context = new System.Text.StringBuilder();
        context.AppendLine(SystemPrompt);
        context.AppendLine();

        foreach (var msg in request.Messages)
        {
            var role = msg.Role.ToLowerInvariant() == "user" ? "User" : "Assistant";
            context.AppendLine($"{role}: {msg.Content}");
        }
        context.AppendLine("Assistant:");

        await foreach (var token in adapter.StreamAsync(context.ToString(), request.MaxTokens, request.Temperature, ct))
        {
            if (token.Meta.IsError)
            {
                yield return new ChatChunkDto
                {
                    Token = null,
                    IsComplete = true,
                    Error = token.Meta.ErrorMessage
                };
                yield break;
            }

            yield return new ChatChunkDto
            {
                Token = token.Token,
                IsComplete = token.IsFinal,
                Error = null
            };
        }
    }

    #region Context Building

    private async Task<string> BuildContextAsync(ChatRequest request)
    {
        var context = new System.Text.StringBuilder();
        context.AppendLine(SystemPrompt);
        context.AppendLine();

        // Add scope context
        if (request.Scope != null)
        {
            context.AppendLine($"Context Scope: {request.Scope.Type}");
            if (!string.IsNullOrEmpty(request.Scope.Path))
            {
                context.AppendLine($"Path: {request.Scope.Path}");
                
                // Include relevant file content if file scope
                if (request.IncludeContext && request.Scope.Type == ContextScope.File)
                {
                    var fileSummary = await _database.GetFileSummaryAsync(request.Scope.Path);
                    if (fileSummary != null)
                    {
                        context.AppendLine($"File Summary: {fileSummary.SummaryText}");
                        if (fileSummary.Functions.Count > 0)
                        {
                            context.AppendLine($"Functions: {string.Join(", ", fileSummary.Functions.Take(10).Select(f => f.Name))}");
                        }
                        if (fileSummary.Todos.Count > 0)
                        {
                            context.AppendLine($"TODOs: {string.Join("; ", fileSummary.Todos.Take(5).Select(t => t.Text))}");
                        }
                    }
                }
            }
            context.AppendLine();
        }

        // Add previous messages (last 6)
        if (request.PreviousMessages != null)
        {
            var recentMessages = request.PreviousMessages.TakeLast(6);
            foreach (var msg in recentMessages)
            {
                var role = msg.Role.ToLowerInvariant() == "user" ? "User" : "Assistant";
                context.AppendLine($"{role}: {msg.Content}");
            }
            context.AppendLine();
        }

        // Add current message
        context.AppendLine($"User: {request.Message}");
        context.AppendLine("Assistant:");

        return context.ToString();
    }

    #endregion

    #region Adapter Selection

    private async Task<ILlmAdapter> GetAdapterAsync(LlmProvider? preferred)
    {
        // Try preferred provider first
        if (preferred.HasValue && preferred.Value != LlmProvider.Mock)
        {
            var key = await _database.GetDecryptedKeyAsync(preferred.Value);
            if (!string.IsNullOrEmpty(key))
            {
                return preferred.Value switch
                {
                    LlmProvider.Gemini => _geminiAdapter.WithKey(key),
                    LlmProvider.OpenAI => _openAiAdapter.WithKey(key),
                    _ => _mockAdapter
                };
            }
        }

        // Try any available provider
        foreach (var provider in new[] { LlmProvider.Gemini, LlmProvider.OpenAI })
        {
            var key = await _database.GetDecryptedKeyAsync(provider);
            if (!string.IsNullOrEmpty(key))
            {
                return provider switch
                {
                    LlmProvider.Gemini => _geminiAdapter.WithKey(key),
                    LlmProvider.OpenAI => _openAiAdapter.WithKey(key),
                    _ => _mockAdapter
                };
            }
        }

        // Fallback to mock
        _logger.LogInformation("No API keys configured, using mock adapter");
        return _mockAdapter;
    }

    private int GetBackoffDelay(int retryCount)
    {
        // Exponential backoff with jitter
        var baseDelay = (int)Math.Pow(2, retryCount) * 500;
        var jitter = Random.Shared.Next(0, 500);
        return baseDelay + jitter;
    }

    #endregion
}

/// <summary>
/// Interface for LLM adapters
/// </summary>
public interface ILlmAdapter
{
    LlmProvider Provider { get; }
    IAsyncEnumerable<StreamToken> StreamAsync(string prompt, int maxTokens, double temperature, CancellationToken ct);
}
