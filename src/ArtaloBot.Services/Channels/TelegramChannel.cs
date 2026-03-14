using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Channels;

/// <summary>
/// Telegram channel using Telegram Bot API.
/// Requires a bot token from @BotFather.
/// </summary>
public class TelegramChannel : IChannelProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IDebugService? _debugService;
    private string _botToken = string.Empty;
    private string _baseUrl = "https://api.telegram.org";
    private CancellationTokenSource? _pollCts;
    private bool _isConnected;
    private long _lastUpdateId;
    private string? _botUsername;

    public ChannelType ChannelType => ChannelType.Telegram;
    public string Name => "Telegram";
    public bool IsConnected => _isConnected;
    public string? BotUsername => _botUsername;

    public event EventHandler<ChannelMessage>? MessageReceived;
    public event EventHandler<TelegramConnectionStatus>? ConnectionStatusChanged;

    public TelegramChannel(IHttpClientFactory? httpClientFactory = null, IDebugService? debugService = null)
    {
        _httpClient = httpClientFactory?.CreateClient("Telegram") ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(60); // Long polling timeout
        _debugService = debugService;
    }

    /// <summary>
    /// Connect to Telegram using Bot API.
    /// Configuration requires "BotToken".
    /// </summary>
    public async Task ConnectAsync(Dictionary<string, string> configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.TryGetValue("BotToken", out var token) || string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("BotToken is required for Telegram connection");
        }

        _botToken = token;
        _debugService?.Info("Telegram", "Connecting to Telegram Bot API...");

        // Verify bot token by getting bot info
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/bot{_botToken}/getMe", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Invalid bot token: {error}");
            }

            var result = await response.Content.ReadFromJsonAsync<TelegramResponse<TelegramUser>>(cancellationToken: cancellationToken);
            if (result?.Ok != true || result.Result == null)
            {
                throw new InvalidOperationException("Failed to get bot info");
            }

            _botUsername = result.Result.Username;
            _isConnected = true;

            _debugService?.Success("Telegram", $"Connected as @{_botUsername}");

            ConnectionStatusChanged?.Invoke(this, new TelegramConnectionStatus
            {
                IsConnected = true,
                BotUsername = _botUsername
            });

            // Start polling for updates
            StartPolling();
        }
        catch (Exception ex)
        {
            _debugService?.Error("Telegram", "Connection failed", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Disconnect from Telegram.
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _debugService?.Info("Telegram", "Disconnecting from Telegram...");

        StopPolling();
        _isConnected = false;
        _botUsername = null;

        ConnectionStatusChanged?.Invoke(this, new TelegramConnectionStatus
        {
            IsConnected = false,
            BotUsername = null
        });

        _debugService?.Success("Telegram", "Disconnected");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send a message to a Telegram chat.
    /// </summary>
    public async Task SendMessageAsync(string recipientId, string message, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Telegram is not connected");
        }

        _debugService?.Info("Telegram", $"Sending message to chat {recipientId}", message);

        var payload = new
        {
            chat_id = recipientId,
            text = message,
            parse_mode = "Markdown"
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/bot{_botToken}/sendMessage",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to send message: {error}");
        }

        _debugService?.Success("Telegram", $"Message sent to chat {recipientId}");
    }

    /// <summary>
    /// Send a message with custom keyboard.
    /// </summary>
    public async Task SendMessageWithKeyboardAsync(
        string chatId,
        string message,
        List<List<string>> keyboard,
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected) throw new InvalidOperationException("Telegram is not connected");

        var keyboardMarkup = new
        {
            keyboard = keyboard.Select(row => row.Select(text => new { text }).ToList()).ToList(),
            resize_keyboard = true,
            one_time_keyboard = true
        };

        var payload = new
        {
            chat_id = chatId,
            text = message,
            parse_mode = "Markdown",
            reply_markup = keyboardMarkup
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_baseUrl}/bot{_botToken}/sendMessage",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to send message: {error}");
        }
    }

    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        _ = PollLoop(_pollCts.Token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private async Task PollLoop(CancellationToken cancellationToken)
    {
        _debugService?.Info("Telegram", "Started polling for updates");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Use long polling with 30 second timeout
                var url = $"{_baseUrl}/bot{_botToken}/getUpdates?offset={_lastUpdateId + 1}&timeout=30";
                var response = await _httpClient.GetAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<TelegramResponse<List<TelegramUpdate>>>(
                        cancellationToken: cancellationToken);

                    if (result?.Ok == true && result.Result != null)
                    {
                        foreach (var update in result.Result)
                        {
                            _lastUpdateId = update.UpdateId;
                            await ProcessUpdateAsync(update);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _debugService?.Warning("Telegram", $"Poll error: {ex.Message}");
                await Task.Delay(5000, cancellationToken); // Wait before retry
            }
        }

        _debugService?.Info("Telegram", "Polling stopped");
    }

    private Task ProcessUpdateAsync(TelegramUpdate update)
    {
        if (update.Message != null)
        {
            var msg = update.Message;
            var senderName = $"{msg.From?.FirstName} {msg.From?.LastName}".Trim();
            if (string.IsNullOrEmpty(senderName))
                senderName = msg.From?.Username ?? "Unknown";

            _debugService?.Info("Telegram", $"Message from {senderName}: {msg.Text}");

            var channelMessage = new ChannelMessage
            {
                ChannelType = ChannelType.Telegram,
                ChannelId = msg.Chat.Id.ToString(),
                SenderId = msg.Chat.Id.ToString(),
                SenderName = senderName,
                Content = msg.Text ?? string.Empty,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(msg.Date).DateTime,
                Metadata = new Dictionary<string, object>
                {
                    ["messageId"] = msg.MessageId,
                    ["chatType"] = msg.Chat.Type,
                    ["userId"] = msg.From?.Id ?? 0,
                    ["username"] = msg.From?.Username ?? string.Empty
                }
            };

            MessageReceived?.Invoke(this, channelMessage);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopPolling();
        _httpClient.Dispose();
    }

    #region DTOs

    private class TelegramResponse<T>
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("result")]
        public T? Result { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    private class TelegramUser
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("is_bot")]
        public bool IsBot { get; set; }

        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = "";

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }

    private class TelegramUpdate
    {
        [JsonPropertyName("update_id")]
        public long UpdateId { get; set; }

        [JsonPropertyName("message")]
        public TelegramMessage? Message { get; set; }
    }

    private class TelegramMessage
    {
        [JsonPropertyName("message_id")]
        public long MessageId { get; set; }

        [JsonPropertyName("from")]
        public TelegramUser? From { get; set; }

        [JsonPropertyName("chat")]
        public TelegramChat Chat { get; set; } = new();

        [JsonPropertyName("date")]
        public long Date { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class TelegramChat
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }

    #endregion
}

public class TelegramConnectionStatus
{
    public bool IsConnected { get; set; }
    public string? BotUsername { get; set; }
}
