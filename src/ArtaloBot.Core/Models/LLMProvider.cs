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
    public string SystemPrompt { get; set; } = "You are a helpful assistant. Answer based on the provided context. Be concise.";
}
