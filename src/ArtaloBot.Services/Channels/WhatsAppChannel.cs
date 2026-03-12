using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace ArtaloBot.Services.Channels;

public class WhatsAppChannel : IChannelProvider
{
    private string? _accountSid;
    private string? _authToken;
    private string? _fromNumber;
    private bool _isConnected;

    public ChannelType ChannelType => ChannelType.WhatsApp;
    public string Name => "WhatsApp";
    public bool IsConnected => _isConnected;

    public event EventHandler<ChannelMessage>? MessageReceived;

    public Task ConnectAsync(Dictionary<string, string> configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.TryGetValue("AccountSid", out _accountSid) ||
            !configuration.TryGetValue("AuthToken", out _authToken) ||
            !configuration.TryGetValue("FromNumber", out _fromNumber))
        {
            throw new ArgumentException("Missing required WhatsApp configuration: AccountSid, AuthToken, FromNumber");
        }

        TwilioClient.Init(_accountSid, _authToken);
        _isConnected = true;

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = false;
        return Task.CompletedTask;
    }

    public async Task SendMessageAsync(string recipientId, string message, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("WhatsApp channel is not connected");
        }

        // Format numbers for WhatsApp
        var from = _fromNumber!.StartsWith("whatsapp:") ? _fromNumber : $"whatsapp:{_fromNumber}";
        var to = recipientId.StartsWith("whatsapp:") ? recipientId : $"whatsapp:{recipientId}";

        await MessageResource.CreateAsync(
            body: message,
            from: new PhoneNumber(from),
            to: new PhoneNumber(to)
        );
    }

    // This method should be called from webhook controller when receiving messages
    public void OnMessageReceived(string senderId, string senderName, string content, Dictionary<string, object>? metadata = null)
    {
        var message = new ChannelMessage
        {
            ChannelId = senderId,
            ChannelType = ChannelType.WhatsApp,
            SenderId = senderId,
            SenderName = senderName,
            Content = content,
            Timestamp = DateTime.UtcNow,
            Metadata = metadata
        };

        MessageReceived?.Invoke(this, message);
    }
}
