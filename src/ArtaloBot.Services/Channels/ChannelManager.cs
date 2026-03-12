using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Channels;

public class ChannelManager : IChannelManager
{
    private readonly Dictionary<ChannelType, IChannelProvider> _providers = [];

    public event EventHandler<ChannelMessage>? MessageReceived;

    public void RegisterProvider(IChannelProvider provider)
    {
        _providers[provider.ChannelType] = provider;
        provider.MessageReceived += OnProviderMessageReceived;
    }

    private void OnProviderMessageReceived(object? sender, ChannelMessage e)
    {
        MessageReceived?.Invoke(this, e);
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
        if (provider == null) return false;

        try
        {
            await provider.ConnectAsync(configuration, cancellationToken);
            return provider.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    public async Task DisconnectChannelAsync(ChannelType type, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(type);
        if (provider != null)
        {
            await provider.DisconnectAsync(cancellationToken);
        }
    }
}
