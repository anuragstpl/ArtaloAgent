namespace ArtaloBot.Core.Models;

public class ChatSession
{
    public int Id { get; set; }
    public string Title { get; set; } = "New Chat";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public LLMProviderType Provider { get; set; } = LLMProviderType.OpenAI;
    public string Model { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public bool IsArchived { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = [];
}
