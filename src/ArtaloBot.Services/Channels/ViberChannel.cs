using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Channels;

/// <summary>
/// Viber channel using Viber Bot API.
/// Popular in Eastern Europe, Middle East, and Southeast Asia.
/// Requires only an auth token from Viber Admin Panel.
/// </summary>
public class ViberChannel : IChannelProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IDebugService? _debugService;
    private string _authToken = string.Empty;
    private const string ApiBaseUrl = "https://chatapi.viber.com/pa";
    private CancellationTokenSource? _pollCts;
    private bool _isConnected;
    private string? _botName;

    public ChannelType ChannelType => ChannelType.Viber;
    public string Name => "Viber";
    public bool IsConnected => _isConnected;
    public string? BotName => _botName;

    public event EventHandler<ChannelMessage>? MessageReceived;
    public event EventHandler<ViberConnectionStatus>? ConnectionStatusChanged;

    public ViberChannel(IHttpClientFactory? httpClientFactory = null, IDebugService? debugService = null)
    {
        _httpClient = httpClientFactory?.CreateClient("Viber") ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _debugService = debugService;
    }

    /// <summary>
    /// Connect to Viber using Bot API.
    /// Configuration requires "AuthToken" from Viber Admin Panel.
    /// </summary>
    public async Task ConnectAsync(Dictionary<string, string> configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.TryGetValue("AuthToken", out var token) || string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("AuthToken is required for Viber connection");
        }

        _authToken = token;
        _debugService?.Info("Viber", "Connecting to Viber Bot API...");

        try
        {
            // Get account info to verify token
            var response = await _httpClient.PostAsJsonAsync(
                $"{ApiBaseUrl}/get_account_info",
                new { auth_token = _authToken },
                cancellationToken);

            var result = await response.Content.ReadFromJsonAsync<ViberResponse<ViberAccountInfo>>(
                cancellationToken: cancellationToken);

            if (result?.Status != 0)
            {
                throw new InvalidOperationException($"Invalid auth token: {result?.StatusMessage}");
            }

            _botName = result.Data?.Name;
            _isConnected = true;

            _debugService?.Success("Viber", $"Connected as {_botName}");

            ConnectionStatusChanged?.Invoke(this, new ViberConnectionStatus
            {
                IsConnected = true,
                BotName = _botName
            });

            // Set webhook would go here in production
            // For now, we'll use polling simulation
        }
        catch (Exception ex)
        {
            _debugService?.Error("Viber", "Connection failed", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Disconnect from Viber.
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _debugService?.Info("Viber", "Disconnecting from Viber...");

        _pollCts?.Cancel();
        _isConnected = false;
        _botName = null;

        ConnectionStatusChanged?.Invoke(this, new ViberConnectionStatus
        {
            IsConnected = false,
            BotName = null
        });

        _debugService?.Success("Viber", "Disconnected");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send a message to a Viber user.
    /// </summary>
    public async Task SendMessageAsync(string recipientId, string message, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Viber is not connected");
        }

        _debugService?.Info("Viber", $"Sending message to {recipientId}", message);

        var payload = new
        {
            auth_token = _authToken,
            receiver = recipientId,
            type = "text",
            text = message,
            sender = new { name = _botName }
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{ApiBaseUrl}/send_message",
            payload,
            cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ViberResponse<object>>(
            cancellationToken: cancellationToken);

        if (result?.Status != 0)
        {
            throw new InvalidOperationException($"Failed to send message: {result?.StatusMessage}");
        }

        _debugService?.Success("Viber", $"Message sent to {recipientId}");
    }

    /// <summary>
    /// Process incoming webhook event.
    /// </summary>
    public void ProcessWebhookEvent(ViberWebhookEvent webhookEvent)
    {
        if (webhookEvent.Event != "message") return;

        var sender = webhookEvent.Sender;
        var messageData = webhookEvent.Message;

        if (sender == null || messageData == null) return;

        _debugService?.Info("Viber", $"Message from {sender.Name}: {messageData.Text}");

        var channelMessage = new ChannelMessage
        {
            ChannelType = ChannelType.Viber,
            ChannelId = sender.Id,
            SenderId = sender.Id,
            SenderName = sender.Name ?? "Unknown",
            Content = messageData.Text ?? string.Empty,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(webhookEvent.Timestamp).DateTime,
            Metadata = new Dictionary<string, object>
            {
                ["messageToken"] = webhookEvent.MessageToken ?? "",
                ["senderAvatar"] = sender.Avatar ?? ""
            }
        };

        MessageReceived?.Invoke(this, channelMessage);
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _httpClient.Dispose();
    }

    #region DTOs

    private class ViberResponse<T>
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("status_message")]
        public string? StatusMessage { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object>? ExtensionData { get; set; }

        public T? Data => ExtensionData != null
            ? System.Text.Json.JsonSerializer.Deserialize<T>(
                System.Text.Json.JsonSerializer.Serialize(ExtensionData))
            : default;
    }

    private class ViberAccountInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("uri")]
        public string Uri { get; set; } = "";

        [JsonPropertyName("subscribers_count")]
        public int SubscribersCount { get; set; }
    }

    #endregion
}

public class ViberConnectionStatus
{
    public bool IsConnected { get; set; }
    public string? BotName { get; set; }
}

public class ViberWebhookEvent
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("message_token")]
    public string? MessageToken { get; set; }

    [JsonPropertyName("sender")]
    public ViberSender? Sender { get; set; }

    [JsonPropertyName("message")]
    public ViberMessage? Message { get; set; }
}

public class ViberSender
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }
}

public class ViberMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
