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
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public double TopP { get; set; } = 1.0;
    public string SystemPrompt { get; set; } = "You are a helpful assistant.";
}
