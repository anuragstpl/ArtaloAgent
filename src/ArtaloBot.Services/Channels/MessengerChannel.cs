using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Channels;

/// <summary>
/// Facebook Messenger channel using Send API.
/// Global platform with billions of users.
/// Requires Page Access Token from Meta Developer Portal.
/// </summary>
public class MessengerChannel : IChannelProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IDebugService? _debugService;
    private string _pageAccessToken = string.Empty;
    private const string ApiBaseUrl = "https://graph.facebook.com/v18.0";
    private bool _isConnected;
    private string? _pageId;
    private string? _pageName;

    public ChannelType ChannelType => ChannelType.Messenger;
    public string Name => "Messenger";
    public bool IsConnected => _isConnected;
    public string? PageName => _pageName;

    public event EventHandler<ChannelMessage>? MessageReceived;
    public event EventHandler<MessengerConnectionStatus>? ConnectionStatusChanged;

    public MessengerChannel(IHttpClientFactory? httpClientFactory = null, IDebugService? debugService = null)
    {
        _httpClient = httpClientFactory?.CreateClient("Messenger") ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _debugService = debugService;
    }

    /// <summary>
    /// Connect to Facebook Messenger using Send API.
    /// Configuration requires "PageAccessToken" from Meta Developer Portal.
    /// </summary>
    public async Task ConnectAsync(Dictionary<string, string> configuration, CancellationToken cancellationToken = default)
    {
        if (!configuration.TryGetValue("PageAccessToken", out var token) || string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("PageAccessToken is required for Messenger connection");
        }

        _pageAccessToken = token;
        _debugService?.Info("Messenger", "Connecting to Facebook Messenger API...");

        try
        {
            // Get page info to verify token
            var response = await _httpClient.GetAsync(
                $"{ApiBaseUrl}/me?fields=id,name&access_token={_pageAccessToken}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Invalid page access token: {error}");
            }

            var pageInfo = await response.Content.ReadFromJsonAsync<MessengerPageInfo>(
                cancellationToken: cancellationToken);

            _pageId = pageInfo?.Id;
            _pageName = pageInfo?.Name;
            _isConnected = true;

            _debugService?.Success("Messenger", $"Connected as {_pageName}");

            ConnectionStatusChanged?.Invoke(this, new MessengerConnectionStatus
            {
                IsConnected = true,
                PageName = _pageName
            });
        }
        catch (Exception ex)
        {
            _debugService?.Error("Messenger", "Connection failed", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Disconnect from Messenger.
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _debugService?.Info("Messenger", "Disconnecting from Messenger...");

        _isConnected = false;
        _pageName = null;
        _pageId = null;

        ConnectionStatusChanged?.Invoke(this, new MessengerConnectionStatus
        {
            IsConnected = false,
            PageName = null
        });

        _debugService?.Success("Messenger", "Disconnected");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Send a message to a Messenger user.
    /// </summary>
    public async Task SendMessageAsync(string recipientId, string message, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("Messenger is not connected");
        }

        _debugService?.Info("Messenger", $"Sending message to {recipientId}", message);

        var payload = new
        {
            recipient = new { id = recipientId },
            message = new { text = message },
            messaging_type = "RESPONSE"
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{ApiBaseUrl}/me/messages?access_token={_pageAccessToken}",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to send message: {error}");
        }

        _debugService?.Success("Messenger", $"Message sent to {recipientId}");
    }

    /// <summary>
    /// Send a message with quick replies.
    /// </summary>
    public async Task SendQuickRepliesAsync(
        string recipientId,
        string message,
        List<string> quickReplies,
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
            throw new InvalidOperationException("Messenger is not connected");

        var payload = new
        {
            recipient = new { id = recipientId },
            message = new
            {
                text = message,
                quick_replies = quickReplies.Select(qr => new
                {
                    content_type = "text",
                    title = qr,
                    payload = qr.ToUpperInvariant().Replace(" ", "_")
                }).ToArray()
            },
            messaging_type = "RESPONSE"
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{ApiBaseUrl}/me/messages?access_token={_pageAccessToken}",
            payload,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to send quick replies: {error}");
        }
    }

    /// <summary>
    /// Process incoming webhook event.
    /// </summary>
    public void ProcessWebhookEvent(MessengerWebhookEntry entry)
    {
        foreach (var messaging in entry.Messaging ?? [])
        {
            if (messaging.Message == null) continue;

            var sender = messaging.Sender;
            var messageData = messaging.Message;

            _debugService?.Info("Messenger", $"Message from {sender?.Id}: {messageData.Text}");

            var channelMessage = new ChannelMessage
            {
                ChannelType = ChannelType.Messenger,
                ChannelId = sender?.Id ?? "",
                SenderId = sender?.Id ?? "",
                SenderName = sender?.Id ?? "Unknown",
                Content = messageData.Text ?? string.Empty,
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(messaging.Timestamp).DateTime,
                Metadata = new Dictionary<string, object>
                {
                    ["mid"] = messageData.Mid ?? "",
                    ["isEcho"] = messageData.IsEcho
                }
            };

            MessageReceived?.Invoke(this, channelMessage);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    #region DTOs

    private class MessengerPageInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    #endregion
}

public class MessengerConnectionStatus
{
    public bool IsConnected { get; set; }
    public string? PageName { get; set; }
}

public class MessengerWebhookEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("messaging")]
    public List<MessengerMessaging>? Messaging { get; set; }
}

public class MessengerMessaging
{
    [JsonPropertyName("sender")]
    public MessengerUser? Sender { get; set; }

    [JsonPropertyName("recipient")]
    public MessengerUser? Recipient { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("message")]
    public MessengerMessageContent? Message { get; set; }
}

public class MessengerUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

public class MessengerMessageContent
{
    [JsonPropertyName("mid")]
    public string? Mid { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("is_echo")]
    public bool IsEcho { get; set; }
}
