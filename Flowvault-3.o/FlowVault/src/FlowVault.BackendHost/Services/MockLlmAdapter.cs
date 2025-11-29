using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using FlowVault.Shared.Models;

namespace FlowVault.BackendHost.Services;

/// <summary>
/// Mock LLM adapter for local testing - simulates streaming responses
/// </summary>
public class MockLlmAdapter : ILlmAdapter
{
    private readonly ILogger<MockLlmAdapter> _logger;
    
    public LlmProvider Provider => LlmProvider.Mock;

    public MockLlmAdapter(ILogger<MockLlmAdapter> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<StreamToken> StreamAsync(
        string prompt, 
        int maxTokens, 
        double temperature, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogDebug("Mock LLM processing prompt ({Length} chars)", prompt.Length);

        // Generate contextual mock response
        var response = GenerateMockResponse(prompt);
        var words = response.Split(' ');
        
        for (int i = 0; i < words.Length && i < maxTokens; i++)
        {
            if (ct.IsCancellationRequested) yield break;

            // Simulate streaming delay (20-80ms per token)
            await Task.Delay(Random.Shared.Next(20, 80), ct);

            var isLast = i == words.Length - 1 || i == maxTokens - 1;
            
            yield return new StreamToken
            {
                Token = (i > 0 ? " " : "") + words[i],
                IsFinal = isLast,
                Meta = new TokenMetadata
                {
                    TokenIndex = i,
                    TotalTokens = words.Length,
                    Provider = LlmProvider.Mock,
                    ModelName = "mock-v1",
                    ConfidenceScore = 0.95
                }
            };
        }
    }

    private string GenerateMockResponse(string prompt)
    {
        var promptLower = prompt.ToLowerInvariant();

        // Priority-related prompts
        if (promptLower.Contains("priorit") || promptLower.Contains("importance"))
        {
            return "Based on the task details, I recommend prioritizing tasks with upcoming deadlines and high importance. " +
                   "Quick wins (low difficulty, high impact) should be tackled first to build momentum. " +
                   "Consider the dependencies between tasks - blocked tasks should have their blockers resolved first.";
        }

        // Schedule-related prompts
        if (promptLower.Contains("schedule") || promptLower.Contains("calendar") || promptLower.Contains("plan"))
        {
            return "I suggest organizing your schedule with focused work blocks in the morning for high-priority tasks. " +
                   "Reserve afternoons for meetings and collaborative work. " +
                   "Build in buffer time between tasks to handle unexpected issues. " +
                   "Remember to take breaks to maintain productivity.";
        }

        // Code/file-related prompts
        if (promptLower.Contains("code") || promptLower.Contains("file") || promptLower.Contains("function"))
        {
            return "Looking at this code, I can identify several key components. " +
                   "The main functions handle data processing and user interaction. " +
                   "There are a few TODOs that should be addressed for code quality. " +
                   "Consider refactoring the larger functions for better maintainability.";
        }

        // Task-related prompts
        if (promptLower.Contains("task") || promptLower.Contains("todo"))
        {
            return "For effective task management, break down large tasks into smaller, actionable items. " +
                   "Set clear deadlines and estimate time requirements. " +
                   "Track dependencies to avoid blockers. " +
                   "Review and adjust priorities regularly based on changing requirements.";
        }

        // Workflow-related prompts
        if (promptLower.Contains("workflow") || promptLower.Contains("graph") || promptLower.Contains("dependency"))
        {
            return "The workflow shows the relationships between tasks. " +
                   "Critical path items are highlighted - these determine the minimum project duration. " +
                   "Consider parallelizing independent tasks to reduce overall time. " +
                   "Focus on resolving blocking dependencies first.";
        }

        // Default response
        return "I'm here to help you with task management, scheduling, and code understanding. " +
               "You can ask me about task priorities, schedule optimization, code summaries, or workflow planning. " +
               "For best results, provide specific context about what you're working on.";
    }
}
