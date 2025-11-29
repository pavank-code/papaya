using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using FlowVault.Shared.Models;

namespace FlowVault.BackendHost.Services;

/// <summary>
/// OpenAI API adapter with streaming support
/// </summary>
public class OpenAiLlmAdapter : ILlmAdapter
{
    private readonly ILogger<OpenAiLlmAdapter> _logger;
    private readonly HttpClient _httpClient;
    private string? _apiKey;

    private const string BaseUrl = "https://api.openai.com/v1/chat/completions";

    public LlmProvider Provider => LlmProvider.OpenAI;

    public OpenAiLlmAdapter(ILogger<OpenAiLlmAdapter> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public OpenAiLlmAdapter WithKey(string apiKey)
    {
        _apiKey = apiKey;
        return this;
    }

    public async IAsyncEnumerable<StreamToken> StreamAsync(
        string prompt,
        int maxTokens,
        double temperature,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            yield return CreateErrorToken("OpenAI API key not configured");
            yield break;
        }

        var channel = Channel.CreateUnbounded<StreamToken>();
        
        // Start the producer task
        var producerTask = ProduceTokensAsync(prompt, maxTokens, temperature, channel.Writer, ct);

        // Consume and yield tokens
        await foreach (var token in channel.Reader.ReadAllAsync(ct))
        {
            yield return token;
        }

        // Await the producer to handle any exceptions
        await producerTask;
    }

    private async Task ProduceTokensAsync(
        string prompt,
        int maxTokens,
        double temperature,
        ChannelWriter<StreamToken> writer,
        CancellationToken ct)
    {
        try
        {
            var request = new OpenAiRequest
            {
                Model = "gpt-4o-mini",
                Messages = new[]
                {
                    new OpenAiMessage { Role = "user", Content = prompt }
                },
                MaxTokens = maxTokens,
                Temperature = temperature,
                Stream = true
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            httpRequest.Content = content;
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            
            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("OpenAI API error: {Status} - {Error}", response.StatusCode, error);
                await writer.WriteAsync(CreateErrorToken($"OpenAI API error: {response.StatusCode}"), ct);
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            var tokenIndex = 0;

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    if (data == "[DONE]") break;

                    var token = ParseStreamChunk(data, ref tokenIndex);
                    if (token != null)
                    {
                        await writer.WriteAsync(token, ct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stream from OpenAI API");
            await writer.WriteAsync(CreateErrorToken($"Stream error: {ex.Message}"), ct);
        }
        finally
        {
            writer.Complete();
        }
    }

    private StreamToken? ParseStreamChunk(string data, ref int tokenIndex)
    {
        try
        {
            var chunk = JsonSerializer.Deserialize<OpenAiStreamResponse>(data);
            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            var finishReason = chunk?.Choices?.FirstOrDefault()?.FinishReason;

            if (!string.IsNullOrEmpty(delta))
            {
                return new StreamToken
                {
                    Token = delta,
                    IsFinal = finishReason == "stop",
                    Meta = new TokenMetadata
                    {
                        TokenIndex = tokenIndex++,
                        Provider = LlmProvider.OpenAI,
                        ModelName = "gpt-4o-mini"
                    }
                };
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse OpenAI stream chunk");
        }
        return null;
    }

    private StreamToken CreateErrorToken(string message)
    {
        return new StreamToken
        {
            Token = "",
            IsFinal = true,
            Meta = new TokenMetadata
            {
                IsError = true,
                ErrorMessage = message,
                Provider = LlmProvider.OpenAI
            }
        };
    }

    #region Request/Response Models

    private class OpenAiRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "gpt-4o-mini";

        [JsonPropertyName("messages")]
        public OpenAiMessage[] Messages { get; set; } = Array.Empty<OpenAiMessage>();

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 2048;

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.7;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = true;
    }

    private class OpenAiMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class OpenAiStreamResponse
    {
        [JsonPropertyName("choices")]
        public OpenAiChoice[]? Choices { get; set; }
    }

    private class OpenAiChoice
    {
        [JsonPropertyName("delta")]
        public OpenAiDelta? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class OpenAiDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    #endregion
}
