using System.ClientModel;
using System.Runtime.CompilerServices;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using OpenAI;
using OpenAI.Chat;
using CoreChatMessage = ArtaloBot.Core.Models.ChatMessage;
using OpenAIChatMessage = OpenAI.Chat.ChatMessage;

namespace ArtaloBot.Services.LLM;

public class OpenAIService : ILLMService
{
    private readonly string _apiKey;
    private readonly string? _baseUrl;
    private ChatClient? _client;
    private string _currentModel = "gpt-4o";

    public LLMProviderType ProviderType => LLMProviderType.OpenAI;
    public string Name => "OpenAI";

    public OpenAIService(string apiKey, string? baseUrl = null)
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl;
    }

    private ChatClient GetClient(string model)
    {
        if (_client == null || _currentModel != model)
        {
            var options = new OpenAIClientOptions();
            if (!string.IsNullOrEmpty(_baseUrl))
            {
                options.Endpoint = new Uri(_baseUrl);
            }

            var client = new OpenAIClient(new ApiKeyCredential(_apiKey), options);
            _client = client.GetChatClient(model);
            _currentModel = model;
        }
        return _client;
    }

    public async Task<string> SendMessageAsync(
        IEnumerable<CoreChatMessage> messages,
        LLMSettings settings,
        CancellationToken cancellationToken = default)
    {
        var client = GetClient(settings.Model);
        var chatMessages = ConvertMessages(messages, settings.SystemPrompt);

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = settings.MaxTokens,
            Temperature = (float)settings.Temperature,
            TopP = (float)settings.TopP
        };

        var completion = await client.CompleteChatAsync(chatMessages, options, cancellationToken);
        return completion.Value.Content[0].Text;
    }

    public async IAsyncEnumerable<string> StreamMessageAsync(
        IEnumerable<CoreChatMessage> messages,
        LLMSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = GetClient(settings.Model);
        var chatMessages = ConvertMessages(messages, settings.SystemPrompt);

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = settings.MaxTokens,
            Temperature = (float)settings.Temperature,
            TopP = (float)settings.TopP
        };

        var stream = client.CompleteChatStreamingAsync(chatMessages, options, cancellationToken);

        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return part.Text;
                }
            }
        }
    }

    public Task<IEnumerable<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = new[]
        {
            "gpt-4o",
            "gpt-4o-mini",
            "gpt-4-turbo",
            "gpt-4",
            "gpt-3.5-turbo",
            "o1",
            "o1-mini",
            "o3-mini"
        };
        return Task.FromResult<IEnumerable<string>>(models);
    }

    public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient("gpt-4o-mini");
            var messages = new List<OpenAIChatMessage>
            {
                new UserChatMessage("Hi")
            };
            var options = new ChatCompletionOptions { MaxOutputTokenCount = 5 };
            await client.CompleteChatAsync(messages, options, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<OpenAIChatMessage> ConvertMessages(IEnumerable<CoreChatMessage> messages, string? systemPrompt)
    {
        var result = new List<OpenAIChatMessage>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            result.Add(new SystemChatMessage(systemPrompt));
        }

        foreach (var msg in messages)
        {
            result.Add(msg.Role switch
            {
                MessageRole.System => new SystemChatMessage(msg.Content),
                MessageRole.User => new UserChatMessage(msg.Content),
                MessageRole.Assistant => new AssistantChatMessage(msg.Content),
                _ => new UserChatMessage(msg.Content)
            });
        }

        return result;
    }
}
