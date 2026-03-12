using ArtaloBot.Core.Models;

namespace ArtaloBot.Core.Interfaces;

public interface ISettingsService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class;
    Task<string?> GetSecureAsync(string key, CancellationToken cancellationToken = default);
    Task SetSecureAsync(string key, string value, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<LLMProvider?> GetProviderSettingsAsync(LLMProviderType type, CancellationToken cancellationToken = default);
    Task SaveProviderSettingsAsync(LLMProvider provider, CancellationToken cancellationToken = default);
    Task<LLMSettings> GetDefaultLLMSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveDefaultLLMSettingsAsync(LLMSettings settings, CancellationToken cancellationToken = default);

    Task<MemorySettings> GetMemorySettingsAsync(CancellationToken cancellationToken = default);
    Task SaveMemorySettingsAsync(MemorySettings settings, CancellationToken cancellationToken = default);
}
