using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Channels;

/// <summary>
/// Manages all communication channels and handles message routing.
/// </summary>
public class ChannelManager : IChannelManager, IDisposable
{
    private readonly Dictionary<ChannelType, IChannelProvider> _providers = [];
    private readonly IDebugService? _debugService;

    // AI integration
    private Func<ChannelMessage, Task<string>>? _aiResponseHandler;

    public event EventHandler<ChannelMessage>? MessageReceived;

    public ChannelManager(IDebugService? debugService = null)
    {
        _debugService = debugService;
    }

    /// <summary>
    /// Set the AI response handler that will process incoming messages.
    /// </summary>
    public void SetAIResponseHandler(Func<ChannelMessage, Task<string>> handler)
    {
        _aiResponseHandler = handler;
        _debugService?.Info("ChannelManager", "AI response handler registered");
    }

    public void RegisterProvider(IChannelProvider provider)
    {
        _providers[provider.ChannelType] = provider;
        provider.MessageReceived += OnProviderMessageReceived;
        _debugService?.Info("ChannelManager", $"Registered provider: {provider.Name}");
    }

    private async void OnProviderMessageReceived(object? sender, ChannelMessage message)
    {
        _debugService?.Info("ChannelManager",
            $"Message from {message.ChannelType}: {message.SenderName}",
            message.Content);

        // Forward to subscribers
        MessageReceived?.Invoke(this, message);

        // Process with AI and respond
        if (_aiResponseHandler != null && sender is IChannelProvider provider)
        {
            try
            {
                _debugService?.Info("ChannelManager", "Processing message with AI...");

                var response = await _aiResponseHandler(message);

                if (!string.IsNullOrEmpty(response))
                {
                    _debugService?.Info("ChannelManager", "Sending AI response", response);
                    await provider.SendMessageAsync(message.SenderId, response);
                    _debugService?.Success("ChannelManager", "Response sent successfully");
                }
            }
            catch (Exception ex)
            {
                _debugService?.Error("ChannelManager", "Failed to process/send response", ex.Message);
            }
        }
    }

    public IEnumerable<IChannelProvider> GetProviders()
    {
        return _providers.Values;
    }

    public IChannelProvider? GetProvider(ChannelType type)
    {
        return _providers.TryGetValue(type, out var provider) ? provider : null;
    }

    public async Task<bool> ConnectChannelAsync(
        ChannelType type,
        Dictionary<string, string> configuration,
        CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(type);
        if (provider == null)
        {
            _debugService?.Error("ChannelManager", $"Provider not found: {type}");
            return false;
        }

        try
        {
            _debugService?.Info("ChannelManager", $"Connecting to {type}...");
            await provider.ConnectAsync(configuration, cancellationToken);
            _debugService?.Success("ChannelManager", $"Connected to {type}");
            return provider.IsConnected;
        }
        catch (Exception ex)
        {
            _debugService?.Error("ChannelManager", $"Failed to connect to {type}", ex.Message);
            return false;
        }
    }

    public async Task DisconnectChannelAsync(ChannelType type, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(type);
        if (provider != null)
        {
            await provider.DisconnectAsync(cancellationToken);
            _debugService?.Info("ChannelManager", $"Disconnected from {type}");
        }
    }

    public void Dispose()
    {
        foreach (var provider in _providers.Values)
        {
            if (provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        _providers.Clear();
    }
}
