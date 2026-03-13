namespace ArtaloBot.Core.Models;

public enum ChannelType
{
    WhatsApp,
    Telegram,
    Discord,
    Slack,
    Teams,
    Direct
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
