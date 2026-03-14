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
/// Slack channel using Slack Web API and Socket Mode.
/// Requires a bot token (xoxb-) and app-level token (xapp-) from Slack.
/// </summary>
public class SlackChannel : IChannelProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IDebugService? _debugService;
    private ClientWebSocket? _webSocket;
    private string _botToken = string.Empty;
    private string _appToken = string.Empty;
    private const string ApiBaseUrl = "https://slack.com/api";
    private CancellationTokenSource? _connectionCts;
    private bool _isConnected;
    private string? _botUserId;
    private string? _botUsername;

    public ChannelType ChannelType => ChannelType.Slack;
    public string Name => "Slack";
    public bool IsConnected => _isConnected;
    public string? BotUsername => _botUsername;

    public event EventHandler<ChannelMessage>? MessageReceived;
    public event EventHandler<SlackConnectionStatus>? ConnectionStatusChanged;

    public SlackChannel(IHttpClientFactory? httpClientFactory = null, IDebugService? debugService = null)
    {
        _httpClient = httpClientFactory?.CreateClient("Slack") ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _debugService = debugService;
    }

    /// <summary>
    /// Connect to Slack using Web API and Socket Mode.
    /// Configuration requires "BotToken" (xoxb-) and "AppToken" (xapp-).
    /// </summary>
    public async Task ConnectAsync(Dictionary<string, string> configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.TryGetValue("BotToken", out var botToken) || string.IsNullOrEmpty(botToken))
        {
            throw new ArgumentException("BotToken (xoxb-) is required for Slack connection");
        }

        if (!configuration.TryGetValue("AppToken", out var appToken) || string.IsNullOrEmpty(appToken))
        {
            throw new ArgumentException("AppToken (xapp-) is required for Slack Socket Mode");
        }

        _botToken = botToken;
        _appToken = appToken;

        _debugService?.Info("Slack", "Connecting to Slack...");

        // Verify bot token and get bot info
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);

            var response = await _httpClient.GetAsync($"{ApiBaseUrl}/auth.test", cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<SlackApiResponse<AuthTestResult>>(cancellationToken: cancellationToken);

            if (result?.Ok != true)
            {
                throw new InvalidOperationException($"Invalid bot token: {result?.Error}");
            }

            _botUserId = result.Data?.UserId;
            _botUsername = result.Data?.User;

            _debugService?.Success("Slack", $"Authenticated as @{_botUsername}");

            // Connect to Socket Mode
            await ConnectToSocketModeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _debugService?.Error("Slack", "Connection failed", ex.Message);
            throw;
        }
    }

    private async Task ConnectToSocketModeAsync(CancellationToken cancellationToken)
    {
        _debugService?.Info("Slack", "Opening Socket Mode connection...");

        // Get WebSocket URL
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/apps.connections.open");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _appToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<SlackApiResponse<ConnectionOpenResult>>(cancellationToken: cancellationToken);

        if (result?.Ok != true || string.IsNullOrEmpty(result.Data?.Url))
        {
            throw new InvalidOperationException($"Failed to get Socket Mode URL: {result?.Error}");
        }

        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _webSocket = new ClientWebSocket();

        await _webSocket.ConnectAsync(new Uri(result.Data.Url), _connectionCts.Token);

        _isConnected = true;
        ConnectionStatusChanged?.Invoke(this, new SlackConnectionStatus
        {
            IsConnected = true,
            BotUsername = _botUsername
        });

        _debugService?.Success("Slack", "Socket Mode connected");

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
                    _debugService?.Warning("Slack", "Socket Mode connection closed");
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await ProcessSocketMessageAsync(json, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _debugService?.Warning("Slack", $"Receive error: {ex.Message}");
                break;
            }
        }

        _isConnected = false;
        ConnectionStatusChanged?.Invoke(this, new SlackConnectionStatus
        {
            IsConnected = false,
            BotUsername = _botUsername
        });

        _debugService?.Info("Slack", "Socket Mode receive loop ended");
    }

    private async Task ProcessSocketMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<SocketModeEnvelope>(json);
            if (envelope == null) return;

            // Acknowledge the message
            if (!string.IsNullOrEmpty(envelope.EnvelopeId))
            {
                await AcknowledgeAsync(envelope.EnvelopeId, cancellationToken);
            }

            switch (envelope.Type)
            {
                case "hello":
                    _debugService?.Info("Slack", "Received hello from Socket Mode");
                    break;

                case "disconnect":
                    _debugService?.Warning("Slack", "Received disconnect request");
                    break;

                case "events_api":
                    await ProcessEventAsync(envelope.Payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            _debugService?.Warning("Slack", $"Failed to process socket message: {ex.Message}");
        }
    }

    private async Task AcknowledgeAsync(string envelopeId, CancellationToken cancellationToken)
    {
        var ack = new { envelope_id = envelopeId };
        var json = JsonSerializer.Serialize(ack);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private Task ProcessEventAsync(JsonElement? payload)
    {
        if (payload == null) return Task.CompletedTask;

        try
        {
            if (!payload.Value.TryGetProperty("event", out var eventData))
                return Task.CompletedTask;

            var eventType = eventData.GetProperty("type").GetString();

            if (eventType == "message")
            {
                // Ignore bot messages and subtypes (edits, deletes, etc.)
                if (eventData.TryGetProperty("bot_id", out _))
                    return Task.CompletedTask;
                if (eventData.TryGetProperty("subtype", out _))
                    return Task.CompletedTask;

                var userId = eventData.GetProperty("user").GetString() ?? "";
                var channel = eventData.GetProperty("channel").GetString() ?? "";
                var text = eventData.GetProperty("text").GetString() ?? "";
                var ts = eventData.GetProperty("ts").GetString() ?? "";

                _debugService?.Info("Slack", $"Message from {userId}: {text}");

                var channelMessage = new ChannelMessage
                {
                    ChannelType = ChannelType.Slack,
                    ChannelId = channel,
                    SenderId = channel, // Reply to channel
                    SenderName = userId, // Will resolve username later
                    Content = text,
                    Timestamp = DateTime.UtcNow,
                    Metadata = new Dictionary<string, object>
                    {
                        ["ts"] = ts,
                        ["userId"] = userId,
                        ["channelId"] = channel
                    }
                };

                MessageReceived?.Invoke(this, channelMessage);
            }
        }
        catch (Exception ex)
        {
            _debugService?.Warning("Slack", $"Failed to process event: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Disconnect from Slack.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _debugService?.Info("Slack", "Disconnecting from Slack...");

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
        _botUserId = null;

        ConnectionStatusChanged?.Invoke(this, new SlackConnectionStatus
        {
            IsConnected = false,
            BotUsername = null
        });

        _debugService?.Success("Slack", "Disconnected");
    }

    /// <summary>
    /// Send a message to a Slack channel.
    /// </summary>
    public async Task SendMessageAsync(string channelId, string message, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Slack is not connected");
        }

        _debugService?.Info("Slack", $"Sending message to channel {channelId}", message);

        var payload = new
        {
            channel = channelId,
            text = message
        };

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
        var response = await _httpClient.PostAsJsonAsync($"{ApiBaseUrl}/chat.postMessage", payload, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<SlackApiResponse<object>>(cancellationToken: cancellationToken);

        if (result?.Ok != true)
        {
            throw new InvalidOperationException($"Failed to send message: {result?.Error}");
        }

        _debugService?.Success("Slack", $"Message sent to channel {channelId}");
    }

    /// <summary>
    /// Send a message with blocks (rich formatting).
    /// </summary>
    public async Task SendBlocksAsync(
        string channelId,
        string fallbackText,
        object[] blocks,
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected) throw new InvalidOperationException("Slack is not connected");

        var payload = new
        {
            channel = channelId,
            text = fallbackText,
            blocks
        };

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
        var response = await _httpClient.PostAsJsonAsync($"{ApiBaseUrl}/chat.postMessage", payload, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<SlackApiResponse<object>>(cancellationToken: cancellationToken);

        if (result?.Ok != true)
        {
            throw new InvalidOperationException($"Failed to send blocks: {result?.Error}");
        }
    }

    /// <summary>
    /// Get user info by ID.
    /// </summary>
    public async Task<string?> GetUsernameAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);
            var response = await _httpClient.GetAsync($"{ApiBaseUrl}/users.info?user={userId}", cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<SlackApiResponse<UserInfoResult>>(cancellationToken: cancellationToken);

            if (result?.Ok == true && result.Data?.User != null)
            {
                return result.Data.User.RealName ?? result.Data.User.Name;
            }
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _webSocket?.Dispose();
        _httpClient.Dispose();
    }

    #region DTOs

    private class SlackApiResponse<T>
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        public T? Data => ExtensionData != null
            ? JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(ExtensionData))
            : default;
    }

    private class AuthTestResult
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = "";

        [JsonPropertyName("user")]
        public string User { get; set; } = "";

        [JsonPropertyName("team")]
        public string Team { get; set; } = "";

        [JsonPropertyName("team_id")]
        public string TeamId { get; set; } = "";
    }

    private class ConnectionOpenResult
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    private class SocketModeEnvelope
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("envelope_id")]
        public string? EnvelopeId { get; set; }

        [JsonPropertyName("payload")]
        public JsonElement? Payload { get; set; }
    }

    private class UserInfoResult
    {
        [JsonPropertyName("user")]
        public SlackUser? User { get; set; }
    }

    private class SlackUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("real_name")]
        public string? RealName { get; set; }
    }

    #endregion
}

public class SlackConnectionStatus
{
    public bool IsConnected { get; set; }
    public string? BotUsername { get; set; }
}
