namespace ArtaloBot.Core.Models;

public enum MessageRole
{
    System,
    User,
    Assistant
}

public class ChatMessage
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LLMProviderType? Provider { get; set; }
    public string? Model { get; set; }
    public int? TokenCount { get; set; }
    public bool IsStreaming { get; set; }
    public bool IsError { get; set; }

    public ChatSession? Session { get; set; }
}
