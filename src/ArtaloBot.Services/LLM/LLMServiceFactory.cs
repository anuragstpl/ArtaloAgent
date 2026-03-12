using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.LLM;

public interface ILLMServiceFactory
{
    ILLMService CreateService(LLMProviderType providerType, string apiKey, string? baseUrl = null);
    ILLMService? GetService(LLMProviderType providerType);
    void RegisterService(LLMProviderType providerType, ILLMService service);
    IEnumerable<LLMProviderType> GetAvailableProviders();
}

public class LLMServiceFactory : ILLMServiceFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDebugService? _debugService;
    private readonly Dictionary<LLMProviderType, ILLMService> _services = [];

    public LLMServiceFactory(IHttpClientFactory httpClientFactory, IDebugService? debugService = null)
    {
        _httpClientFactory = httpClientFactory;
        _debugService = debugService;
    }

    public ILLMService CreateService(LLMProviderType providerType, string apiKey, string? baseUrl = null)
    {
        ILLMService service = providerType switch
        {
            LLMProviderType.OpenAI => new OpenAIService(apiKey, baseUrl),
            LLMProviderType.Gemini => new GeminiService(apiKey),
            LLMProviderType.Ollama => new OllamaService(
                _httpClientFactory.CreateClient("Ollama"),
                baseUrl ?? "http://localhost:11434",
                _debugService),
            _ => throw new ArgumentException($"Unknown provider type: {providerType}")
        };

        _services[providerType] = service;
        return service;
    }

    public ILLMService? GetService(LLMProviderType providerType)
    {
        return _services.TryGetValue(providerType, out var service) ? service : null;
    }

    public void RegisterService(LLMProviderType providerType, ILLMService service)
    {
        _services[providerType] = service;
    }

    public IEnumerable<LLMProviderType> GetAvailableProviders()
    {
        return _services.Keys;
    }
}
