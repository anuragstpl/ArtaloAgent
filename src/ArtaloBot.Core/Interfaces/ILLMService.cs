using ArtaloBot.Core.Models;

namespace ArtaloBot.Core.Interfaces;

public interface ILLMService
{
    LLMProviderType ProviderType { get; }
    string Name { get; }

    Task<string> SendMessageAsync(
        IEnumerable<ChatMessage> messages,
        LLMSettings settings,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> StreamMessageAsync(
        IEnumerable<ChatMessage> messages,
        LLMSettings settings,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<string>> GetAvailableModelsAsync(
        CancellationToken cancellationToken = default);

    Task<bool> ValidateConnectionAsync(
        CancellationToken cancellationToken = default);
}
