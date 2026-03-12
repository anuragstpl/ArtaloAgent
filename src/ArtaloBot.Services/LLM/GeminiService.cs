using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.LLM;

public class GeminiService : ILLMService
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public LLMProviderType ProviderType => LLMProviderType.Gemini;
    public string Name => "Google Gemini";

    public GeminiService(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient();
    }

    public async Task<string> SendMessageAsync(
        IEnumerable<ChatMessage> messages,
        LLMSettings settings,
        CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, settings);
        var url = $"{BaseUrl}/{settings.Model}:generateContent?key={_apiKey}";

        var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: cancellationToken);
        return result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        IEnumerable<ChatMessage> messages,
        LLMSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = BuildRequest(messages, settings);
        var url = $"{BaseUrl}/{settings.Model}:streamGenerateContent?key={_apiKey}&alt=sse";

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var jsonData = line[6..];
            if (jsonData == "[DONE]") break;

            var text = ParseStreamChunk(jsonData);
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private static string? ParseStreamChunk(string jsonData)
    {
        try
        {
            var chunk = JsonSerializer.Deserialize<GeminiResponse>(jsonData);
            return chunk?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        }
        catch
        {
            return null;
        }
    }

    public Task<IEnumerable<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = new[]
        {
            "gemini-2.0-flash",
            "gemini-2.0-flash-lite",
            "gemini-1.5-pro",
            "gemini-1.5-flash",
            "gemini-1.5-flash-8b"
        };
        return Task.FromResult<IEnumerable<string>>(models);
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new[] { new ChatMessage { Role = MessageRole.User, Content = "Hi" } };
            var settings = new LLMSettings { Model = "gemini-1.5-flash", MaxTokens = 10 };
            await SendMessageAsync(messages, settings, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static GeminiRequest BuildRequest(IEnumerable<ChatMessage> messages, LLMSettings settings)
    {
        var contents = new List<GeminiContent>();

        foreach (var msg in messages)
        {
            var role = msg.Role switch
            {
                MessageRole.User => "user",
                MessageRole.Assistant => "model",
                _ => "user"
            };

            var content = msg.Content;
            if (msg.Role == MessageRole.User && !string.IsNullOrEmpty(settings.SystemPrompt) && contents.Count == 0)
            {
                content = $"System: {settings.SystemPrompt}\n\nUser: {msg.Content}";
            }

            contents.Add(new GeminiContent
            {
                Role = role,
                Parts = [new GeminiPart { Text = content }]
            });
        }

        return new GeminiRequest
        {
            Contents = contents,
            GenerationConfig = new GeminiGenerationConfig
            {
                MaxOutputTokens = settings.MaxTokens,
                Temperature = settings.Temperature,
                TopP = settings.TopP
            }
        };
    }

    #region DTOs

    private class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = [];

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = [];
    }

    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private class GeminiGenerationConfig
    {
        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("topP")]
        public double TopP { get; set; }
    }

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    #endregion
}
