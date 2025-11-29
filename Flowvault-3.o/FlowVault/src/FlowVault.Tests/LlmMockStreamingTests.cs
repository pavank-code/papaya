using Xunit;
using FlowVault.BackendHost.Services;
using FlowVault.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowVault.Tests;

public class LlmMockStreamingTests
{
    private readonly ILogger<MockLlmAdapter> _logger = NullLogger<MockLlmAdapter>.Instance;

    [Fact]
    public async Task MockAdapterStreamsTokens()
    {
        // Arrange
        var adapter = new MockLlmAdapter(_logger);
        var prompt = "Hello, how are you?";

        // Act
        var tokens = new List<StreamToken>();
        await foreach (var token in adapter.StreamAsync(prompt, 100, 0.7, CancellationToken.None))
        {
            tokens.Add(token);
        }

        // Assert
        Assert.NotEmpty(tokens);
        Assert.True(tokens.Count > 1, "Should stream multiple tokens");
        Assert.True(tokens.Any(t => !string.IsNullOrEmpty(t.Token)), "Should have tokens with content");
    }

    [Fact]
    public async Task MockAdapterHandlesCancellation()
    {
        // Arrange
        var adapter = new MockLlmAdapter(_logger);
        var prompt = "Tell me a long story about programming";
        var cts = new CancellationTokenSource();

        // Act
        var tokens = new List<StreamToken>();
        var task = Task.Run(async () =>
        {
            await foreach (var token in adapter.StreamAsync(prompt, 1000, 0.7, cts.Token))
            {
                tokens.Add(token);
                if (tokens.Count >= 5)
                {
                    cts.Cancel();
                    break;
                }
            }
        });

        // Assert - should complete without throwing (graceful cancellation)
        await task;
        Assert.True(tokens.Count >= 5);
    }

    [Fact]
    public async Task MockAdapterCompletesStream()
    {
        // Arrange
        var adapter = new MockLlmAdapter(_logger);
        var prompt = "Hi";

        // Act
        StreamToken? lastToken = null;
        await foreach (var token in adapter.StreamAsync(prompt, 100, 0.7, CancellationToken.None))
        {
            lastToken = token;
        }

        // Assert
        Assert.NotNull(lastToken);
    }

    [Fact]
    public void MockAdapterReportsCorrectProvider()
    {
        // Arrange
        var adapter = new MockLlmAdapter(_logger);

        // Assert
        Assert.Equal(LlmProvider.Mock, adapter.Provider);
    }

    [Fact]
    public async Task MockAdapterHandlesEmptyPrompt()
    {
        // Arrange
        var adapter = new MockLlmAdapter(_logger);
        var prompt = "";

        // Act
        var tokens = new List<StreamToken>();
        await foreach (var token in adapter.StreamAsync(prompt, 100, 0.7, CancellationToken.None))
        {
            tokens.Add(token);
        }

        // Assert - should still produce some output
        Assert.NotEmpty(tokens);
    }
}
