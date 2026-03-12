using ArtaloBot.Core.Models;

namespace ArtaloBot.Core.Interfaces;

public interface IChannelProvider
{
    ChannelType ChannelType { get; }
    string Name { get; }
    bool IsConnected { get; }

    Task ConnectAsync(Dictionary<string, string> configuration, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task SendMessageAsync(string recipientId, string message, CancellationToken cancellationToken = default);

    event EventHandler<ChannelMessage>? MessageReceived;
}

public interface IChannelManager
{
    IEnumerable<IChannelProvider> GetProviders();
    IChannelProvider? GetProvider(ChannelType type);
    Task<bool> ConnectChannelAsync(ChannelType type, Dictionary<string, string> configuration, CancellationToken cancellationToken = default);
    Task DisconnectChannelAsync(ChannelType type, CancellationToken cancellationToken = default);

    event EventHandler<ChannelMessage>? MessageReceived;
}
