using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.LLM;

public class OllamaService : ILLMService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly IDebugService? _debugService;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public LLMProviderType ProviderType => LLMProviderType.Ollama;
    public string Name => "Ollama (Local)";

    public OllamaService(HttpClient httpClient, string baseUrl = "http://localhost:11434", IDebugService? debugService = null)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _debugService = debugService;
    }

    public async Task<string> SendMessageAsync(
        IEnumerable<ChatMessage> messages,
        LLMSettings settings,
        CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest
        {
            Model = settings.Model,
            Messages = ConvertMessages(messages, settings.SystemPrompt),
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = settings.Temperature,
                NumPredict = settings.MaxTokens,
                TopP = settings.TopP
            }
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<OllamaChatResponse>(responseJson, JsonOptions);
        return result?.Message?.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        IEnumerable<ChatMessage> messages,
        LLMSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest
        {
            Model = settings.Model,
            Messages = ConvertMessages(messages, settings.SystemPrompt),
            Stream = true,
            Options = new OllamaOptions
            {
                Temperature = settings.Temperature,
                NumPredict = settings.MaxTokens > 0 ? settings.MaxTokens : 512, // Default to 512 for fast responses
                TopP = settings.TopP
            }
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        _debugService?.Info("Ollama", "Sending streaming request", $"Model: {settings.Model}, Prompt length: {json.Length} chars");

        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = content
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _debugService?.Error("Ollama", "Request timed out - Ollama may be overloaded or the model is too slow");
            throw new InvalidOperationException("Ollama request timed out. Try using a smaller/faster model like qwen2.5:3b or phi3:mini", ex);
        }
        catch (HttpRequestException ex)
        {
            _debugService?.Error("Ollama", $"Connection error: {ex.Message}");
            throw new InvalidOperationException($"Cannot connect to Ollama at {_baseUrl}. Make sure Ollama is running.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _debugService?.Error("Ollama", $"HTTP {(int)response.StatusCode}: {errorContent}");
                throw new InvalidOperationException($"Ollama error: {response.StatusCode} - {errorContent}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            _debugService?.Info("Ollama", "Streaming response started...");
            var chunkCount = 0;

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(line)) continue;

                OllamaChatResponse? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line, JsonOptions);
                }
                catch (JsonException ex)
                {
                    _debugService?.Warning("Ollama", $"Failed to parse chunk: {ex.Message}", line);
                    continue;
                }

                if (!string.IsNullOrEmpty(chunk?.Message?.Content))
                {
                    chunkCount++;
                    yield return chunk.Message.Content;
                }

                if (chunk?.Done == true)
                {
                    _debugService?.Success("Ollama", $"Streaming complete", $"Total chunks: {chunkCount}");
                    break;
                }
            }
        }
    }

    public async Task<IEnumerable<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Enumerable.Empty<string>();
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<OllamaModelsResponse>(json, JsonOptions);

            return result?.Models?.Select(m => m.Name) ?? Enumerable.Empty<string>();
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private List<OllamaMessage> ConvertMessages(IEnumerable<ChatMessage> messages, string? systemPrompt)
    {
        var result = new List<OllamaMessage>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            result.Add(new OllamaMessage { Role = "system", Content = systemPrompt });
            _debugService?.Info("Ollama", "System prompt added to request",
                $"Length: {systemPrompt.Length} chars\nPreview: {systemPrompt.Substring(0, Math.Min(200, systemPrompt.Length))}...");
        }
        else
        {
            _debugService?.Warning("Ollama", "No system prompt provided");
        }

        foreach (var msg in messages)
        {
            result.Add(new OllamaMessage
            {
                Role = msg.Role switch
                {
                    MessageRole.System => "system",
                    MessageRole.User => "user",
                    MessageRole.Assistant => "assistant",
                    _ => "user"
                },
                Content = msg.Content
            });
        }

        _debugService?.Info("Ollama", $"Total messages in request: {result.Count}",
            string.Join("\n", result.Select(m => $"  [{m.Role}]: {m.Content.Substring(0, Math.Min(50, m.Content.Length))}...")));

        return result;
    }

    #region DTOs

    private class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<OllamaMessage> Messages { get; set; } = [];

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaOptions? Options { get; set; }
    }

    private class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }
    }

    private class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }

    private class OllamaModelsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModel>? Models { get; set; }
    }

    private class OllamaModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}
