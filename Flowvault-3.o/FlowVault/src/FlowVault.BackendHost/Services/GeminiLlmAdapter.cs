using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using FlowVault.Shared.Models;

namespace FlowVault.BackendHost.Services;

/// <summary>
/// Gemini API adapter with streaming support
/// </summary>
public class GeminiLlmAdapter : ILlmAdapter
{
    private readonly ILogger<GeminiLlmAdapter> _logger;
    private readonly HttpClient _httpClient;
    private string? _apiKey;

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:streamGenerateContent";

    public LlmProvider Provider => LlmProvider.Gemini;

    public GeminiLlmAdapter(ILogger<GeminiLlmAdapter> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public GeminiLlmAdapter WithKey(string apiKey)
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
            yield return CreateErrorToken("Gemini API key not configured");
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
            var url = $"{BaseUrl}?key={_apiKey}&alt=sse";
            var request = new GeminiRequest
            {
                Contents = new[]
                {
                    new GeminiContent
                    {
                        Parts = new[] { new GeminiPart { Text = prompt } }
                    }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    MaxOutputTokens = maxTokens,
                    Temperature = temperature
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            using var response = await _httpClient.PostAsync(url, content, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Gemini API error: {Status} - {Error}", response.StatusCode, error);
                await writer.WriteAsync(CreateErrorToken($"Gemini API error: {response.StatusCode}"), ct);
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
            _logger.LogError(ex, "Failed to stream from Gemini API");
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
            var chunk = JsonSerializer.Deserialize<GeminiStreamResponse>(data);
            var text = chunk?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            
            if (!string.IsNullOrEmpty(text))
            {
                return new StreamToken
                {
                    Token = text,
                    IsFinal = chunk?.Candidates?.FirstOrDefault()?.FinishReason == "STOP",
                    Meta = new TokenMetadata
                    {
                        TokenIndex = tokenIndex++,
                        Provider = LlmProvider.Gemini,
                        ModelName = "gemini-2.0-flash"
                    }
                };
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Gemini stream chunk");
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
                Provider = LlmProvider.Gemini
            }
        };
    }

    #region Request/Response Models

    private class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();
        
        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private class GeminiGenerationConfig
    {
        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; } = 2048;
        
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.7;
    }

    private class GeminiStreamResponse
    {
        [JsonPropertyName("candidates")]
        public GeminiCandidate[]? Candidates { get; set; }
    }

    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
        
        [JsonPropertyName("finishReason")]
        public string? FinishReason { get; set; }
    }

    #endregion
}
