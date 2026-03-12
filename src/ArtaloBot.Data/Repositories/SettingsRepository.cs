using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ArtaloBot.Data.Repositories;

public class SettingsRepository : ISettingsService
{
    private readonly AppDbContext _context;
    private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("ArtaloBot_Secure_Storage_2024");

    public SettingsRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var setting = await _context.AppSettings.FindAsync([key], cancellationToken);
        if (setting == null) return null;

        var value = setting.IsEncrypted ? Decrypt(setting.Value) : setting.Value;
        return JsonSerializer.Deserialize<T>(value);
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        var json = JsonSerializer.Serialize(value);
        var setting = await _context.AppSettings.FindAsync([key], cancellationToken);

        if (setting == null)
        {
            setting = new AppSetting { Key = key, Value = json };
            _context.AppSettings.Add(setting);
        }
        else
        {
            setting.Value = json;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetSecureAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _context.AppSettings.FindAsync([key], cancellationToken);
        if (setting == null) return null;

        return setting.IsEncrypted ? Decrypt(setting.Value) : setting.Value;
    }

    public async Task SetSecureAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var encrypted = Encrypt(value);
        var setting = await _context.AppSettings.FindAsync([key], cancellationToken);

        if (setting == null)
        {
            setting = new AppSetting
            {
                Key = key,
                Value = encrypted,
                IsEncrypted = true
            };
            _context.AppSettings.Add(setting);
        }
        else
        {
            setting.Value = encrypted;
            setting.IsEncrypted = true;
            setting.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _context.AppSettings.FindAsync([key], cancellationToken);
        if (setting != null)
        {
            _context.AppSettings.Remove(setting);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<LLMProvider?> GetProviderSettingsAsync(LLMProviderType type, CancellationToken cancellationToken = default)
    {
        var key = $"provider_{type}";
        var provider = await GetAsync<LLMProvider>(key, cancellationToken);

        if (provider != null)
        {
            var apiKey = await GetSecureAsync($"apikey_{type}", cancellationToken);
            provider.ApiKey = apiKey ?? string.Empty;
        }

        return provider;
    }

    public async Task SaveProviderSettingsAsync(LLMProvider provider, CancellationToken cancellationToken = default)
    {
        var key = $"provider_{provider.Type}";

        // Store API key separately with encryption
        if (!string.IsNullOrEmpty(provider.ApiKey))
        {
            await SetSecureAsync($"apikey_{provider.Type}", provider.ApiKey, cancellationToken);
        }

        // Store provider settings without API key
        var settingsToStore = new LLMProvider
        {
            Type = provider.Type,
            Name = provider.Name,
            DisplayName = provider.DisplayName,
            BaseUrl = provider.BaseUrl,
            DefaultModel = provider.DefaultModel,
            IsEnabled = provider.IsEnabled,
            AvailableModels = provider.AvailableModels
        };

        await SetAsync(key, settingsToStore, cancellationToken);
    }

    public async Task<LLMSettings> GetDefaultLLMSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<LLMSettings>("default_llm_settings", cancellationToken)
               ?? new LLMSettings();
    }

    public async Task SaveDefaultLLMSettingsAsync(LLMSettings settings, CancellationToken cancellationToken = default)
    {
        await SetAsync("default_llm_settings", settings, cancellationToken);
    }

    public async Task<MemorySettings> GetMemorySettingsAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<MemorySettings>("memory_settings", cancellationToken)
               ?? new MemorySettings();
    }

    public async Task SaveMemorySettingsAsync(MemorySettings settings, CancellationToken cancellationToken = default)
    {
        await SetAsync("memory_settings", settings, cancellationToken);
    }

    private static string Encrypt(string plainText)
    {
        var data = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(data, _entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Decrypt(string encryptedText)
    {
        try
        {
            var data = Convert.FromBase64String(encryptedText);
            var decrypted = ProtectedData.Unprotect(data, _entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return encryptedText;
        }
    }
}
