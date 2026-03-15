namespace ArtaloBot.Core.Models;

public enum ChannelType
{
    WhatsApp,
    Telegram,
    Discord,
    Slack,
    Teams,
    Direct,
    Viber,      // Popular in Eastern Europe, Middle East, Southeast Asia
    Line,       // Popular in Japan, Thailand, Taiwan, Indonesia
    Messenger   // Facebook Messenger - Global
}

public class Channel
{
    public int Id { get; set; }
    public ChannelType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Dictionary<string, string> Configuration { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ChannelMessage
{
    public string ChannelId { get; set; } = string.Empty;
    public ChannelType ChannelType { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// LLM configuration for a specific channel.
/// Allows different channels to use different LLM providers and models.
/// </summary>
public class ChannelLLMConfig
{
    public int Id { get; set; }
    public ChannelType ChannelType { get; set; }
    public LLMProviderType Provider { get; set; } = LLMProviderType.Ollama;
    public string Model { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1024;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
