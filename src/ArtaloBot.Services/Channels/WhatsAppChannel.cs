using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Channels;

/// <summary>
/// WhatsApp channel using Baileys bridge for QR code authentication.
/// Communicates with a local Node.js bridge that handles WhatsApp Web connection.
/// </summary>
public class WhatsAppChannel : IChannelProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IDebugService? _debugService;
    private string _bridgeUrl = "http://localhost:3847";
    private Process? _bridgeProcess;
    private CancellationTokenSource? _pollCts;
    private bool _isConnected;
    private string? _connectedNumber;
    private string? _currentQrCode;

    public ChannelType ChannelType => ChannelType.WhatsApp;
    public string Name => "WhatsApp";
    public bool IsConnected => _isConnected;
    public string? ConnectedNumber => _connectedNumber;
    public string? QrCode => _currentQrCode;

    public event EventHandler<ChannelMessage>? MessageReceived;
    public event EventHandler<string>? QrCodeGenerated;
    public event EventHandler<WhatsAppConnectionStatus>? ConnectionStatusChanged;

    public WhatsAppChannel(IHttpClientFactory? httpClientFactory = null, IDebugService? debugService = null)
    {
        _httpClient = httpClientFactory?.CreateClient("WhatsApp") ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _debugService = debugService;
    }

    /// <summary>
    /// Start the WhatsApp bridge and initiate connection.
    /// </summary>
    public async Task ConnectAsync(Dictionary<string, string> configuration, CancellationToken cancellationToken = default)
    {
        if (configuration.TryGetValue("BridgeUrl", out var url))
        {
            _bridgeUrl = url;
        }

        _debugService?.Info("WhatsApp", "Starting WhatsApp connection...", _bridgeUrl);

        // Check if bridge is running
        var bridgeRunning = await CheckBridgeHealthAsync(cancellationToken);

        if (!bridgeRunning)
        {
            _debugService?.Warning("WhatsApp", "Bridge not running. Please start it manually.",
                "Run: cd src/ArtaloBot.WhatsAppBridge && npm install && npm start");
            throw new InvalidOperationException(
                "WhatsApp bridge is not running. Please start it first:\n" +
                "1. Open terminal in src/ArtaloBot.WhatsAppBridge\n" +
                "2. Run: npm install\n" +
                "3. Run: npm start");
        }

        // Request connection (generates QR)
        try
        {
            var response = await _httpClient.PostAsync($"{_bridgeUrl}/connect", null, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<BridgeResponse>(cancellationToken: cancellationToken);

            _debugService?.Info("WhatsApp", $"Connection initiated: {result?.Status}");

            // Start polling for status and messages
            StartPolling();
        }
        catch (Exception ex)
        {
            _debugService?.Error("WhatsApp", "Failed to initiate connection", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Disconnect from WhatsApp and stop the bridge.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _debugService?.Info("WhatsApp", "Disconnecting from WhatsApp...");

        StopPolling();

        try
        {
            await _httpClient.PostAsync($"{_bridgeUrl}/disconnect", null, cancellationToken);
        }
        catch { /* Ignore if bridge is not running */ }

        _isConnected = false;
        _connectedNumber = null;
        _currentQrCode = null;

        ConnectionStatusChanged?.Invoke(this, new WhatsAppConnectionStatus
        {
            Status = "disconnected",
            ConnectedNumber = null
        });

        _debugService?.Success("WhatsApp", "Disconnected");
    }

    /// <summary>
    /// Send a message to a WhatsApp user.
    /// </summary>
    public async Task SendMessageAsync(string recipientId, string message, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            throw new InvalidOperationException("WhatsApp is not connected");
        }

        _debugService?.Info("WhatsApp", $"Sending message to {recipientId}", message);

        var content = JsonContent.Create(new { recipientId, message });
        var response = await _httpClient.PostAsync($"{_bridgeUrl}/send", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to send message: {error}");
        }

        _debugService?.Success("WhatsApp", $"Message sent to {recipientId}");
    }

    /// <summary>
    /// Get current QR code for scanning.
    /// </summary>
    public async Task<string?> GetQrCodeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_bridgeUrl}/qr", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<QrCodeResponse>(cancellationToken: cancellationToken);
                return result?.QrCode;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Get current connection status.
    /// </summary>
    public async Task<WhatsAppConnectionStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_bridgeUrl}/status", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<WhatsAppConnectionStatus>(cancellationToken: cancellationToken);
                return result ?? new WhatsAppConnectionStatus { Status = "unknown" };
            }
        }
        catch { }
        return new WhatsAppConnectionStatus { Status = "error" };
    }

    private async Task<bool> CheckBridgeHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_bridgeUrl}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
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
        _debugService?.Info("WhatsApp", "Started polling for status and messages");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Poll status
                var status = await GetStatusAsync(cancellationToken);

                var previouslyConnected = _isConnected;
                _isConnected = status.Status == "connected";
                _connectedNumber = status.ConnectedNumber;

                // Check for QR code
                if (status.Status == "waiting_for_scan" && status.HasQrCode)
                {
                    var qr = await GetQrCodeAsync(cancellationToken);
                    if (qr != null && qr != _currentQrCode)
                    {
                        _currentQrCode = qr;
                        QrCodeGenerated?.Invoke(this, qr);
                        _debugService?.Info("WhatsApp", "New QR code generated");
                    }
                }
                else
                {
                    _currentQrCode = null;
                }

                // Notify status change
                if (_isConnected != previouslyConnected || status.Status == "waiting_for_scan")
                {
                    ConnectionStatusChanged?.Invoke(this, status);
                }

                // Poll messages if connected
                if (_isConnected)
                {
                    await PollMessagesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _debugService?.Warning("WhatsApp", $"Poll error: {ex.Message}");
            }

            await Task.Delay(1000, cancellationToken); // Poll every second
        }

        _debugService?.Info("WhatsApp", "Polling stopped");
    }

    private async Task PollMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_bridgeUrl}/messages", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<MessagesResponse>(cancellationToken: cancellationToken);

                foreach (var msg in result?.Messages ?? [])
                {
                    _debugService?.Info("WhatsApp", $"Message from {msg.SenderName}: {msg.Content}");

                    var channelMessage = new ChannelMessage
                    {
                        ChannelType = ChannelType.WhatsApp,
                        ChannelId = msg.SenderId,
                        SenderId = msg.SenderId,
                        SenderName = msg.SenderName,
                        Content = msg.Content,
                        Timestamp = DateTime.Parse(msg.Timestamp),
                        Metadata = new Dictionary<string, object>
                        {
                            ["senderJid"] = msg.SenderJid,
                            ["isGroup"] = msg.IsGroup
                        }
                    };

                    MessageReceived?.Invoke(this, channelMessage);
                }
            }
        }
        catch (Exception ex)
        {
            _debugService?.Warning("WhatsApp", $"Failed to poll messages: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopPolling();
        _bridgeProcess?.Kill();
        _bridgeProcess?.Dispose();
        _httpClient.Dispose();
    }

    #region DTOs

    private class BridgeResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "";

        [JsonPropertyName("connectedNumber")]
        public string? ConnectedNumber { get; set; }
    }

    private class QrCodeResponse
    {
        [JsonPropertyName("qrCode")]
        public string? QrCode { get; set; }
    }

    private class MessagesResponse
    {
        [JsonPropertyName("messages")]
        public List<BridgeMessage> Messages { get; set; } = [];
    }

    private class BridgeMessage
    {
        [JsonPropertyName("senderId")]
        public string SenderId { get; set; } = "";

        [JsonPropertyName("senderJid")]
        public string SenderJid { get; set; } = "";

        [JsonPropertyName("senderName")]
        public string SenderName { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = "";

        [JsonPropertyName("isGroup")]
        public bool IsGroup { get; set; }
    }

    #endregion
}

public class WhatsAppConnectionStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("connectedNumber")]
    public string? ConnectedNumber { get; set; }

    [JsonPropertyName("hasQrCode")]
    public bool HasQrCode { get; set; }
}
