using Microsoft.Extensions.Logging;
using FlowVault.Shared.Models;
using FlowVault.BackendHost.Persistence;

namespace FlowVault.BackendHost.Services;

/// <summary>
/// API key management service
/// </summary>
public class ApiKeyService
{
    private readonly ILogger<ApiKeyService> _logger;
    private readonly DatabaseService _database;
    private readonly HttpClient _httpClient;

    public ApiKeyService(ILogger<ApiKeyService> logger, DatabaseService database)
    {
        _logger = logger;
        _database = database;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// Get an API key for a provider (returns decrypted key)
    /// </summary>
    public async Task<string?> GetKeyAsync(LlmProvider provider, CancellationToken ct)
    {
        return await _database.GetDecryptedKeyAsync(provider);
    }

    /// <summary>
    /// Save an API key for a provider
    /// </summary>
    public async Task SaveKeyAsync(LlmProvider provider, string key, CancellationToken ct)
    {
        var apiKey = new ApiKeyDto
        {
            Id = Guid.NewGuid().ToString(),
            Provider = provider,
            DisplayName = $"{provider} API Key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _database.SaveApiKeyAsync(apiKey, key);
        _logger.LogInformation("Saved API key for {Provider}", provider);
    }

    /// <summary>
    /// Delete an API key for a provider
    /// </summary>
    public async Task DeleteKeyAsync(LlmProvider provider, CancellationToken ct)
    {
        var keys = await _database.GetApiKeysAsync();
        var key = keys.FirstOrDefault(k => k.Provider == provider);
        if (key != null)
        {
            await _database.DeleteApiKeyAsync(key.Id);
            _logger.LogInformation("Deleted API key for {Provider}", provider);
        }
    }

    /// <summary>
    /// Test an API key connection
    /// </summary>
    public async Task<bool> TestKeyAsync(LlmProvider provider, string key, CancellationToken ct)
    {
        var result = provider switch
        {
            LlmProvider.Gemini => await TestGeminiKeyAsync(key, ct),
            LlmProvider.OpenAI => await TestOpenAiKeyAsync(key, ct),
            LlmProvider.Mock => new ApiKeyTestResult { Success = true },
            _ => new ApiKeyTestResult { Success = false, Message = "Unknown provider" }
        };

        return result.Success;
    }

    /// <summary>
    /// Test an API key connection (with full ApiKeyDto)
    /// </summary>
    public async Task<ApiKeyTestResult> TestApiKeyAsync(ApiKeyDto apiKey, string? plainKey, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrEmpty(plainKey))
            {
                return new ApiKeyTestResult
                {
                    Success = false,
                    Message = "No API key provided"
                };
            }

            var result = apiKey.Provider switch
            {
                LlmProvider.Gemini => await TestGeminiKeyAsync(plainKey, ct),
                LlmProvider.OpenAI => await TestOpenAiKeyAsync(plainKey, ct),
                LlmProvider.Mock => new ApiKeyTestResult { Success = true, Message = "Mock adapter ready", ModelName = "mock-v1" },
                _ => new ApiKeyTestResult { Success = false, Message = "Unknown provider" }
            };

            stopwatch.Stop();
            result.ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;

            // Update test status in database
            apiKey.LastTestedAt = DateTime.UtcNow;
            apiKey.LastTestSuccess = result.Success;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "API key test failed for {Provider}", apiKey.Provider);
            return new ApiKeyTestResult
            {
                Success = false,
                Message = "Connection test failed",
                ProviderError = ex.Message,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// Save an API key (encrypted)
    /// </summary>
    public async Task SaveApiKeyAsync(ApiKeyDto apiKey, string? plainKey, CancellationToken ct)
    {
        await _database.SaveApiKeyAsync(apiKey, plainKey);
        _logger.LogInformation("Saved API key for {Provider}", apiKey.Provider);
    }

    /// <summary>
    /// Get all API keys (masked)
    /// </summary>
    public async Task<IEnumerable<ApiKeyDto>> GetApiKeysAsync(CancellationToken ct)
    {
        return await _database.GetApiKeysAsync();
    }

    /// <summary>
    /// Delete an API key by ID
    /// </summary>
    public async Task DeleteApiKeyAsync(string keyId, CancellationToken ct)
    {
        await _database.DeleteApiKeyAsync(keyId);
        _logger.LogInformation("Deleted API key {Id}", keyId);
    }

    #region Provider-specific Tests

    private async Task<ApiKeyTestResult> TestGeminiKeyAsync(string apiKey, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";

        try
        {
            var response = await _httpClient.GetAsync(url, ct);

            if (response.IsSuccessStatusCode)
            {
                return new ApiKeyTestResult
                {
                    Success = true,
                    Message = "Connection successful",
                    ModelName = "gemini-2.0-flash"
                };
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            return new ApiKeyTestResult
            {
                Success = false,
                Message = $"API returned {response.StatusCode}",
                ProviderError = error
            };
        }
        catch (HttpRequestException ex)
        {
            return new ApiKeyTestResult
            {
                Success = false,
                Message = "Connection failed",
                ProviderError = ex.Message
            };
        }
    }

    private async Task<ApiKeyTestResult> TestOpenAiKeyAsync(string apiKey, CancellationToken ct)
    {
        var url = "https://api.openai.com/v1/models";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = await _httpClient.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return new ApiKeyTestResult
                {
                    Success = true,
                    Message = "Connection successful",
                    ModelName = "gpt-4o-mini"
                };
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            return new ApiKeyTestResult
            {
                Success = false,
                Message = $"API returned {response.StatusCode}",
                ProviderError = error
            };
        }
        catch (HttpRequestException ex)
        {
            return new ApiKeyTestResult
            {
                Success = false,
                Message = "Connection failed",
                ProviderError = ex.Message
            };
        }
    }

    #endregion
}
