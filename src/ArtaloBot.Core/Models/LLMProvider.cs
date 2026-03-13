namespace ArtaloBot.Core.Models;

public enum LLMProviderType
{
    OpenAI,
    Gemini,
    Ollama
}

public class LLMProvider
{
    public LLMProviderType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public List<string> AvailableModels { get; set; } = [];
}

public class LLMSettings
{
    public string Model { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.3; // Lower temperature for more accurate/factual responses
    public int MaxTokens { get; set; } = 1024; // Reduced for faster responses
    public double TopP { get; set; } = 0.9;
    public string SystemPrompt { get; set; } = @"You are a helpful assistant.

IMPORTANT: Maintain conversation context. The user may:
- Send multiple messages before you respond
- Refer to previous messages (e.g., 'yes', 'do that', 'the first one')
- Continue a thought from earlier messages

Always consider the FULL conversation history when responding. If the user says 'yes', 'ok', 'do it', etc., refer back to what was previously discussed.";
}
