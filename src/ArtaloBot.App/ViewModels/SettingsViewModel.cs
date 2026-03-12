using System.Collections.ObjectModel;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using ArtaloBot.Services.LLM;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArtaloBot.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILLMServiceFactory _llmFactory;
    private readonly IMemoryService _memoryService;

    [ObservableProperty]
    private ObservableCollection<ProviderSettingsViewModel> _providers = [];

    [ObservableProperty]
    private ProviderSettingsViewModel? _selectedProvider;

    [ObservableProperty]
    private LLMSettings _defaultSettings = new();

    [ObservableProperty]
    private MemorySettings _memorySettings = new();

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _totalMemoryCount;

    public ObservableCollection<string> EmbeddingProviders { get; } =
    [
        "simple",
        "ollama",
        "openai"
    ];

    public ObservableCollection<string> OllamaEmbeddingModels { get; } =
    [
        "nomic-embed-text",
        "mxbai-embed-large",
        "all-minilm",
        "snowflake-arctic-embed"
    ];

    public ObservableCollection<string> OpenAIEmbeddingModels { get; } =
    [
        "text-embedding-3-small",
        "text-embedding-3-large",
        "text-embedding-ada-002"
    ];

    public SettingsViewModel(
        ISettingsService settingsService,
        ILLMServiceFactory llmFactory,
        IMemoryService memoryService)
    {
        _settingsService = settingsService;
        _llmFactory = llmFactory;
        _memoryService = memoryService;

        InitializeProviders();
    }

    private void InitializeProviders()
    {
        Providers =
        [
            new ProviderSettingsViewModel
            {
                Type = LLMProviderType.OpenAI,
                Name = "OpenAI",
                DisplayName = "OpenAI (GPT-4, GPT-3.5)",
                Icon = "Robot",
                DefaultModels = ["gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-3.5-turbo", "o1", "o1-mini"]
            },
            new ProviderSettingsViewModel
            {
                Type = LLMProviderType.Gemini,
                Name = "Gemini",
                DisplayName = "Google Gemini",
                Icon = "StarFourPoints",
                DefaultModels = ["gemini-2.0-flash", "gemini-1.5-pro", "gemini-1.5-flash"]
            },
            new ProviderSettingsViewModel
            {
                Type = LLMProviderType.Ollama,
                Name = "Ollama",
                DisplayName = "Ollama (Local)",
                Icon = "Server",
                BaseUrl = "http://localhost:11434",
                DefaultModels = ["llama3.2", "llama3.1", "mistral", "codellama", "phi3"]
            }
        ];

        SelectedProvider = Providers.FirstOrDefault();
    }

    public async Task LoadSettingsAsync()
    {
        DefaultSettings = await _settingsService.GetDefaultLLMSettingsAsync();
        MemorySettings = await _settingsService.GetMemorySettingsAsync();

        foreach (var provider in Providers)
        {
            var settings = await _settingsService.GetProviderSettingsAsync(provider.Type);
            if (settings != null)
            {
                provider.ApiKey = settings.ApiKey;
                provider.BaseUrl = settings.BaseUrl;
                provider.DefaultModel = settings.DefaultModel;
                provider.IsEnabled = settings.IsEnabled;
            }
        }

        await RefreshMemoryCountAsync();
    }

    private async Task RefreshMemoryCountAsync()
    {
        try
        {
            TotalMemoryCount = await _memoryService.GetMemoryCountAsync();
        }
        catch { /* non-critical */ }
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        IsSaving = true;
        StatusMessage = "Saving...";

        try
        {
            foreach (var provider in Providers)
            {
                var llmProvider = new LLMProvider
                {
                    Type = provider.Type,
                    Name = provider.Name,
                    DisplayName = provider.DisplayName,
                    ApiKey = provider.ApiKey,
                    BaseUrl = provider.BaseUrl,
                    DefaultModel = provider.DefaultModel,
                    IsEnabled = provider.IsEnabled,
                    AvailableModels = provider.DefaultModels.ToList()
                };

                await _settingsService.SaveProviderSettingsAsync(llmProvider);

                // Initialize the service if API key is provided
                if (!string.IsNullOrEmpty(provider.ApiKey) || provider.Type == LLMProviderType.Ollama)
                {
                    _llmFactory.CreateService(provider.Type, provider.ApiKey, provider.BaseUrl);
                }
            }

            await _settingsService.SaveDefaultLLMSettingsAsync(DefaultSettings);
            await _settingsService.SaveMemorySettingsAsync(MemorySettings);

            StatusMessage = "Settings saved successfully!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task TestConnection(ProviderSettingsViewModel provider)
    {
        provider.IsTestingConnection = true;
        provider.ConnectionStatus = "Testing...";

        try
        {
            var service = _llmFactory.CreateService(provider.Type, provider.ApiKey, provider.BaseUrl);
            var isValid = await service.ValidateConnectionAsync();

            provider.ConnectionStatus = isValid ? "Connected!" : "Connection failed";
            provider.IsConnected = isValid;

            if (isValid)
            {
                var models = await service.GetAvailableModelsAsync();
                provider.AvailableModels.Clear();
                foreach (var model in models)
                {
                    provider.AvailableModels.Add(model);
                }
            }
        }
        catch (Exception ex)
        {
            provider.ConnectionStatus = $"Error: {ex.Message}";
            provider.IsConnected = false;
        }
        finally
        {
            provider.IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private async Task ClearAllMemories()
    {
        try
        {
            StatusMessage = "Clearing all memories...";
            await _memoryService.ClearAllMemoriesAsync();
            await RefreshMemoryCountAsync();
            StatusMessage = "All memories cleared.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error clearing memories: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestEmbedding()
    {
        try
        {
            StatusMessage = "Testing embedding provider...";
            // Save current memory settings first so the service uses the latest config
            await _settingsService.SaveMemorySettingsAsync(MemorySettings);

            var embedding = await _memoryService.GetEmbeddingAsync("Hello, this is a test.");
            StatusMessage = $"Embedding OK — dimension: {embedding.Length}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Embedding error: {ex.Message}";
        }
    }
}

public partial class ProviderSettingsViewModel : ObservableObject
{
    public LLMProviderType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<string> DefaultModels { get; set; } = [];

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _defaultModel = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = [];

    public bool RequiresApiKey => Type != LLMProviderType.Ollama;
    public bool HasBaseUrl => Type == LLMProviderType.Ollama || Type == LLMProviderType.OpenAI;
}
