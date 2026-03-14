using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Channels;

/// <summary>
/// Discord channel using Discord Bot API with Gateway WebSocket.
/// Requires a bot token from Discord Developer Portal.
/// </summary>
public class DiscordChannel : IChannelProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IDebugService? _debugService;
    private ClientWebSocket? _webSocket;
    private string _botToken = string.Empty;
    private const string ApiBaseUrl = "https://discord.com/api/v10";
    private const string GatewayUrl = "wss://gateway.discord.gg/?v=10&encoding=json";
    private CancellationTokenSource? _connectionCts;
    private bool _isConnected;
    private string? _botUsername;
    private string? _botId;
    private int _heartbeatInterval;
    private int? _lastSequence;
    private string? _sessionId;

    public ChannelType ChannelType => ChannelType.Discord;
    public string Name => "Discord";
    public bool IsConnected => _isConnected;
    public string? BotUsername => _botUsername;

    public event EventHandler<ChannelMessage>? MessageReceived;
    public event EventHandler<DiscordConnectionStatus>? ConnectionStatusChanged;

    public DiscordChannel(IHttpClientFactory? httpClientFactory = null, IDebugService? debugService = null)
    {
        _httpClient = httpClientFactory?.CreateClient("Discord") ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _debugService = debugService;
    }

    /// <summary>
    /// Connect to Discord using Bot API and Gateway.
    /// Configuration requires "BotToken".
    /// </summary>
    public async Task ConnectAsync(Dictionary<string, string> configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.TryGetValue("BotToken", out var token) || string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("BotToken is required for Discord connection");
        }

        _botToken = token;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", _botToken);

        _debugService?.Info("Discord", "Connecting to Discord...");

        // Verify bot token by getting bot info
        try
        {
            var response = await _httpClient.GetAsync($"{ApiBaseUrl}/users/@me", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Invalid bot token: {error}");
            }

            var user = await response.Content.ReadFromJsonAsync<DiscordUser>(cancellationToken: cancellationToken);
            if (user == null)
            {
                throw new InvalidOperationException("Failed to get bot info");
            }

            _botUsername = user.Username;
            _botId = user.Id;

            _debugService?.Success("Discord", $"Authenticated as {_botUsername}");

            // Connect to Gateway
            await ConnectToGatewayAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _debugService?.Error("Discord", "Connection failed", ex.Message);
            throw;
        }
    }

    private async Task ConnectToGatewayAsync(CancellationToken cancellationToken)
    {
        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _webSocket = new ClientWebSocket();

        _debugService?.Info("Discord", "Connecting to Gateway...");

        await _webSocket.ConnectAsync(new Uri(GatewayUrl), _connectionCts.Token);

        // Start receiving messages
        _ = ReceiveLoopAsync(_connectionCts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _debugService?.Warning("Discord", "Gateway connection closed");
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await ProcessGatewayMessageAsync(json, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _debugService?.Warning("Discord", $"Receive error: {ex.Message}");
                break;
            }
        }

        _isConnected = false;
        ConnectionStatusChanged?.Invoke(this, new DiscordConnectionStatus
        {
            IsConnected = false,
            BotUsername = _botUsername
        });

        _debugService?.Info("Discord", "Gateway receive loop ended");
    }

    private async Task ProcessGatewayMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<GatewayPayload>(json);
            if (payload == null) return;

            if (payload.Sequence.HasValue)
                _lastSequence = payload.Sequence;

            switch (payload.OpCode)
            {
                case 10: // Hello
                    var helloData = JsonSerializer.Deserialize<HelloPayload>(payload.Data.GetRawText());
                    if (helloData != null)
                    {
                        _heartbeatInterval = helloData.HeartbeatInterval;
                        _ = HeartbeatLoopAsync(cancellationToken);
                        await SendIdentifyAsync(cancellationToken);
                    }
                    break;

                case 0: // Dispatch
                    await HandleDispatchAsync(payload.EventName, payload.Data);
                    break;

                case 11: // Heartbeat ACK
                    // Heartbeat acknowledged
                    break;

                case 7: // Reconnect
                    _debugService?.Warning("Discord", "Gateway requested reconnect");
                    break;

                case 9: // Invalid Session
                    _debugService?.Warning("Discord", "Invalid session");
                    break;
            }
        }
        catch (Exception ex)
        {
            _debugService?.Warning("Discord", $"Failed to process gateway message: {ex.Message}");
        }
    }

    private async Task HandleDispatchAsync(string? eventName, JsonElement data)
    {
        switch (eventName)
        {
            case "READY":
                _sessionId = data.GetProperty("session_id").GetString();
                _isConnected = true;
                _debugService?.Success("Discord", "Gateway connected and ready");
                ConnectionStatusChanged?.Invoke(this, new DiscordConnectionStatus
                {
                    IsConnected = true,
                    BotUsername = _botUsername
                });
                break;

            case "MESSAGE_CREATE":
                await ProcessMessageAsync(data);
                break;
        }
    }

    private Task ProcessMessageAsync(JsonElement data)
    {
        try
        {
            // Ignore bot messages
            var author = data.GetProperty("author");
            if (author.TryGetProperty("bot", out var botProp) && botProp.GetBoolean())
                return Task.CompletedTask;

            var authorId = author.GetProperty("id").GetString() ?? "";
            var authorName = author.GetProperty("username").GetString() ?? "Unknown";
            var channelId = data.GetProperty("channel_id").GetString() ?? "";
            var content = data.GetProperty("content").GetString() ?? "";
            var messageId = data.GetProperty("id").GetString() ?? "";

            // Get guild ID if present
            string? guildId = null;
            if (data.TryGetProperty("guild_id", out var guildProp))
                guildId = guildProp.GetString();

            _debugService?.Info("Discord", $"Message from {authorName}: {content}");

            var channelMessage = new ChannelMessage
            {
                ChannelType = ChannelType.Discord,
                ChannelId = channelId,
                SenderId = channelId, // Reply to channel
                SenderName = authorName,
                Content = content,
                Timestamp = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    ["messageId"] = messageId,
                    ["authorId"] = authorId,
                    ["guildId"] = guildId ?? "",
                    ["isDM"] = guildId == null
                }
            };

            MessageReceived?.Invoke(this, channelMessage);
        }
        catch (Exception ex)
        {
            _debugService?.Warning("Discord", $"Failed to process message: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task SendIdentifyAsync(CancellationToken cancellationToken)
    {
        var identify = new
        {
            op = 2,
            d = new
            {
                token = _botToken,
                intents = 33281, // GUILDS | GUILD_MESSAGES | DIRECT_MESSAGES | MESSAGE_CONTENT
                properties = new
                {
                    os = "windows",
                    browser = "ArtaloBot",
                    device = "ArtaloBot"
                }
            }
        };

        var json = JsonSerializer.Serialize(identify);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);

        _debugService?.Info("Discord", "Sent identify payload");
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await Task.Delay(_heartbeatInterval, cancellationToken);

                var heartbeat = new { op = 1, d = _lastSequence };
                var json = JsonSerializer.Serialize(heartbeat);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _debugService?.Warning("Discord", $"Heartbeat error: {ex.Message}");
                break;
            }
        }
    }

    /// <summary>
    /// Disconnect from Discord.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _debugService?.Info("Discord", "Disconnecting from Discord...");

        _connectionCts?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", cancellationToken);
            }
            catch { }
        }

        _isConnected = false;
        _botUsername = null;
        _sessionId = null;

        ConnectionStatusChanged?.Invoke(this, new DiscordConnectionStatus
        {
            IsConnected = false,
            BotUsername = null
        });

        _debugService?.Success("Discord", "Disconnected");
    }

    /// <summary>
    /// Send a message to a Discord channel.
    /// </summary>
    public async Task SendMessageAsync(string channelId, string message, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Discord is not connected");
        }

        _debugService?.Info("Discord", $"Sending message to channel {channelId}", message);

        var payload = new { content = message };
        var response = await _httpClient.PostAsJsonAsync(
            $"{ApiBaseUrl}/channels/{channelId}/messages",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to send message: {error}");
        }

        _debugService?.Success("Discord", $"Message sent to channel {channelId}");
    }

    /// <summary>
    /// Send a message with embed.
    /// </summary>
    public async Task SendEmbedAsync(
        string channelId,
        string title,
        string description,
        int color = 0x5865F2,
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected) throw new InvalidOperationException("Discord is not connected");

        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title,
                    description,
                    color
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{ApiBaseUrl}/channels/{channelId}/messages",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to send embed: {error}");
        }
    }

    public void Dispose()
    {
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _webSocket?.Dispose();
        _httpClient.Dispose();
    }

    #region DTOs

    private class DiscordUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("discriminator")]
        public string Discriminator { get; set; } = "";

        [JsonPropertyName("bot")]
        public bool Bot { get; set; }
    }

    private class GatewayPayload
    {
        [JsonPropertyName("op")]
        public int OpCode { get; set; }

        [JsonPropertyName("d")]
        public JsonElement Data { get; set; }

        [JsonPropertyName("s")]
        public int? Sequence { get; set; }

        [JsonPropertyName("t")]
        public string? EventName { get; set; }
    }

    private class HelloPayload
    {
        [JsonPropertyName("heartbeat_interval")]
        public int HeartbeatInterval { get; set; }
    }

    #endregion
}

public class DiscordConnectionStatus
{
    public bool IsConnected { get; set; }
    public string? BotUsername { get; set; }
}
