namespace FlowVault.Shared.Models;

/// <summary>
/// Chat message data transfer object
/// </summary>
public class ChatMessageDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = "user"; // "user", "assistant", "system"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Chat request for streaming
/// </summary>
public class ChatRequest
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
    public string Message { get; set; } = string.Empty;
    public ContextScopeDto? Scope { get; set; }
    public bool IncludeContext { get; set; } = true;
    public List<string>? AttachedFilePaths { get; set; }
    public List<ChatMessageDto>? PreviousMessages { get; set; }
    public LlmProvider? PreferredProvider { get; set; }
    public int MaxTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.7;
}

/// <summary>
/// Streaming token response
/// </summary>
public class StreamToken
{
    public string Token { get; set; } = string.Empty;
    public bool IsFinal { get; set; }
    public TokenMetadata Meta { get; set; } = new();
}

/// <summary>
/// Token metadata
/// </summary>
public class TokenMetadata
{
    public int TokenIndex { get; set; }
    public int TotalTokens { get; set; }
    public LlmProvider Provider { get; set; }
    public string? ModelName { get; set; }
    public double? ConfidenceScore { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Conversation container
/// </summary>
public class ConversationDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TileId { get; set; } = string.Empty;
    public TileType TileType { get; set; }
    public ContextScopeDto? Scope { get; set; }
    public List<ChatMessageDto> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Simplified chat request for streaming
/// </summary>
public class ChatRequestDto
{
    public List<ChatMessageDto> Messages { get; set; } = new();
    public LlmProvider Provider { get; set; } = LlmProvider.Mock;
    public int MaxTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.7;
}

/// <summary>
/// Streaming chunk response
/// </summary>
public class ChatChunkDto
{
    public string? Token { get; set; }
    public bool IsComplete { get; set; }
    public string? Error { get; set; }
}
