using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Channels;

/// <summary>
/// LINE channel using LINE Messaging API.
/// Popular in Japan, Thailand, Taiwan, and Indonesia.
/// Requires Channel Access Token from LINE Developers Console.
/// </summary>
public class LineChannel : IChannelProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IDebugService? _debugService;
    private string _channelAccessToken = string.Empty;
    private const string ApiBaseUrl = "https://api.line.me/v2";
    private bool _isConnected;
    private string? _botId;
    private string? _botName;

    public ChannelType ChannelType => ChannelType.Line;
    public string Name => "LINE";
    public bool IsConnected => _isConnected;
    public string? BotName => _botName;

    public event EventHandler<ChannelMessage>? MessageReceived;
    public event EventHandler<LineConnectionStatus>? ConnectionStatusChanged;

    public LineChannel(IHttpClientFactory? httpClientFactory = null, IDebugService? debugService = null)
    {
        _httpClient = httpClientFactory?.CreateClient("LINE") ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _debugService = debugService;
    }

    /// <summary>
    /// Connect to LINE using Messaging API.
    /// Configuration requires "ChannelAccessToken" from LINE Developers Console.
    /// </summary>
    public async Task ConnectAsync(Dictionary<string, string> configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.TryGetValue("ChannelAccessToken", out var token) || string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("ChannelAccessToken is required for LINE connection");
        }

        _channelAccessToken = token;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _channelAccessToken);

        _debugService?.Info("LINE", "Connecting to LINE Messaging API...");

        try
        {
            // Get bot info to verify token
            var response = await _httpClient.GetAsync($"{ApiBaseUrl}/bot/info", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Invalid channel access token: {error}");
            }

            var botInfo = await response.Content.ReadFromJsonAsync<LineBotInfo>(cancellationToken: cancellationToken);

            _botId = botInfo?.UserId;
            _botName = botInfo?.DisplayName ?? botInfo?.BasicId;
            _isConnected = true;

            _debugService?.Success("LINE", $"Connected as {_botName}");

            ConnectionStatusChanged?.Invoke(this, new LineConnectionStatus
            {
                IsConnected = true,
                BotName = _botName
            });
        }
        catch (Exception ex)
        {
            _debugService?.Error("LINE", "Connection failed", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Disconnect from LINE.
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _debugService?.Info("LINE", "Disconnecting from LINE...");

        _isConnected = false;
        _botName = null;
        _botId = null;

        ConnectionStatusChanged?.Invoke(this, new LineConnectionStatus
        {
            IsConnected = false,
            BotName = null
        });

        _debugService?.Success("LINE", "Disconnected");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send a message to a LINE user.
    /// </summary>
    public async Task SendMessageAsync(string recipientId, string message, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("LINE is not connected");
        }

        _debugService?.Info("LINE", $"Sending message to {recipientId}", message);

        var payload = new
        {
            to = recipientId,
            messages = new[]
            {
                new { type = "text", text = message }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{ApiBaseUrl}/bot/message/push",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to send message: {error}");
        }

        _debugService?.Success("LINE", $"Message sent to {recipientId}");
    }

    /// <summary>
    /// Reply to a message using reply token (for webhook responses).
    /// </summary>
    public async Task ReplyMessageAsync(string replyToken, string message, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("LINE is not connected");
        }

        var payload = new
        {
            replyToken,
            messages = new[]
            {
                new { type = "text", text = message }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{ApiBaseUrl}/bot/message/reply",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to reply: {error}");
        }
    }

    /// <summary>
    /// Process incoming webhook event.
    /// </summary>
    public void ProcessWebhookEvent(LineWebhookEvent webhookEvent)
    {
        if (webhookEvent.Type != "message") return;
        if (webhookEvent.Message?.Type != "text") return;

        var source = webhookEvent.Source;
        var messageData = webhookEvent.Message;

        _debugService?.Info("LINE", $"Message from {source?.UserId}: {messageData.Text}");

        var channelMessage = new ChannelMessage
        {
            ChannelType = ChannelType.Line,
            ChannelId = source?.UserId ?? "",
            SenderId = source?.UserId ?? "",
            SenderName = source?.UserId ?? "Unknown", // LINE doesn't provide name in webhook
            Content = messageData.Text ?? string.Empty,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(webhookEvent.Timestamp).DateTime,
            Metadata = new Dictionary<string, object>
            {
                ["replyToken"] = webhookEvent.ReplyToken ?? "",
                ["messageId"] = messageData.Id ?? "",
                ["sourceType"] = source?.Type ?? ""
            }
        };

        MessageReceived?.Invoke(this, channelMessage);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    #region DTOs

    private class LineBotInfo
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; } = "";

        [JsonPropertyName("basicId")]
        public string BasicId { get; set; } = "";

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("pictureUrl")]
        public string? PictureUrl { get; set; }
    }

    #endregion
}

public class LineConnectionStatus
{
    public bool IsConnected { get; set; }
    public string? BotName { get; set; }
}

public class LineWebhookEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("replyToken")]
    public string? ReplyToken { get; set; }

    [JsonPropertyName("source")]
    public LineSource? Source { get; set; }

    [JsonPropertyName("message")]
    public LineMessageContent? Message { get; set; }
}

public class LineSource
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("groupId")]
    public string? GroupId { get; set; }

    [JsonPropertyName("roomId")]
    public string? RoomId { get; set; }
}

public class LineMessageContent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
