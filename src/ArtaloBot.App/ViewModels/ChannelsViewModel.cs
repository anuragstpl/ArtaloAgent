using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using ArtaloBot.Services.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArtaloBot.App.ViewModels;

public partial class ChannelsViewModel : ObservableObject
{
    private readonly IChannelManager? _channelManager;
    private readonly ISettingsService _settingsService;
    private readonly IAgentService? _agentService;
    private readonly IDebugService? _debugService;
    private readonly WhatsAppChannel? _whatsAppChannel;
    private readonly TelegramChannel? _telegramChannel;
    private readonly DiscordChannel? _discordChannel;
    private readonly SlackChannel? _slackChannel;

    [ObservableProperty]
    private ObservableCollection<ChannelConfigViewModel> _channels = [];

    [ObservableProperty]
    private ChannelConfigViewModel? _selectedChannel;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // All available agents for assignment
    [ObservableProperty]
    private ObservableCollection<AgentAssignmentViewModel> _availableAgents = [];

    // WhatsApp specific
    [ObservableProperty]
    private bool _showQrCode;

    [ObservableProperty]
    private BitmapImage? _qrCodeImage;

    [ObservableProperty]
    private string? _connectedNumber;

    [ObservableProperty]
    private string _whatsAppStatus = "Not Connected";

    // Telegram specific
    [ObservableProperty]
    private string _telegramStatus = "Not Connected";

    // Discord specific
    [ObservableProperty]
    private string _discordStatus = "Not Connected";

    // Slack specific
    [ObservableProperty]
    private string _slackStatus = "Not Connected";

    public ChannelsViewModel(
        ISettingsService settingsService,
        IChannelManager? channelManager = null,
        IAgentService? agentService = null,
        IDebugService? debugService = null)
    {
        _settingsService = settingsService;
        _channelManager = channelManager;
        _agentService = agentService;
        _debugService = debugService;

        // Get channel providers from manager
        _whatsAppChannel = channelManager?.GetProvider(ChannelType.WhatsApp) as WhatsAppChannel;
        _telegramChannel = channelManager?.GetProvider(ChannelType.Telegram) as TelegramChannel;
        _discordChannel = channelManager?.GetProvider(ChannelType.Discord) as DiscordChannel;
        _slackChannel = channelManager?.GetProvider(ChannelType.Slack) as SlackChannel;

        // WhatsApp events
        if (_whatsAppChannel != null)
        {
            _whatsAppChannel.QrCodeGenerated += OnQrCodeGenerated;
            _whatsAppChannel.ConnectionStatusChanged += OnWhatsAppConnectionStatusChanged;
            _whatsAppChannel.MessageReceived += OnMessageReceived;
        }

        // Telegram events
        if (_telegramChannel != null)
        {
            _telegramChannel.ConnectionStatusChanged += OnTelegramConnectionStatusChanged;
            _telegramChannel.MessageReceived += OnMessageReceived;
        }

        // Discord events
        if (_discordChannel != null)
        {
            _discordChannel.ConnectionStatusChanged += OnDiscordConnectionStatusChanged;
            _discordChannel.MessageReceived += OnMessageReceived;
        }

        // Slack events
        if (_slackChannel != null)
        {
            _slackChannel.ConnectionStatusChanged += OnSlackConnectionStatusChanged;
            _slackChannel.MessageReceived += OnMessageReceived;
        }

        InitializeChannels();
    }

    private void InitializeChannels()
    {
        Channels =
        [
            new ChannelConfigViewModel
            {
                Type = ChannelType.WhatsApp,
                Name = "WhatsApp",
                Icon = "Whatsapp",
                Description = "Scan QR code to connect your WhatsApp",
                UsesQrCode = true,
                ConfigurationFields = []
            },
            new ChannelConfigViewModel
            {
                Type = ChannelType.Telegram,
                Name = "Telegram",
                Icon = "Telegram",
                Description = "Connect via Telegram Bot API. Get your bot token from @BotFather",
                ConfigurationFields =
                [
                    new ConfigFieldViewModel { Key = "BotToken", Label = "Bot Token", Placeholder = "123456789:ABCdefGHI...", IsRequired = true, IsPassword = true }
                ]
            },
            new ChannelConfigViewModel
            {
                Type = ChannelType.Discord,
                Name = "Discord",
                Icon = "Discord",
                Description = "Connect via Discord Bot. Create a bot at Discord Developer Portal",
                ConfigurationFields =
                [
                    new ConfigFieldViewModel { Key = "BotToken", Label = "Bot Token", Placeholder = "MTIz...", IsRequired = true, IsPassword = true }
                ]
            },
            new ChannelConfigViewModel
            {
                Type = ChannelType.Slack,
                Name = "Slack",
                Icon = "Slack",
                Description = "Connect to Slack workspace using Socket Mode",
                ConfigurationFields =
                [
                    new ConfigFieldViewModel { Key = "BotToken", Label = "Bot Token (xoxb-)", Placeholder = "xoxb-...", IsRequired = true, IsPassword = true },
                    new ConfigFieldViewModel { Key = "AppToken", Label = "App Token (xapp-)", Placeholder = "xapp-...", IsRequired = true, IsPassword = true }
                ]
            },
            new ChannelConfigViewModel
            {
                Type = ChannelType.Teams,
                Name = "Microsoft Teams",
                Icon = "MicrosoftTeams",
                Description = "Connect to Microsoft Teams",
                IsComingSoon = true,
                ConfigurationFields = []
            }
        ];

        SelectedChannel = Channels.FirstOrDefault();
    }

    public async Task LoadAgentsAsync()
    {
        if (_agentService == null) return;

        try
        {
            var agents = await _agentService.GetAllAgentsAsync();

            AvailableAgents.Clear();
            foreach (var agent in agents.Where(a => a.IsEnabled))
            {
                AvailableAgents.Add(new AgentAssignmentViewModel
                {
                    AgentId = agent.Id,
                    AgentName = agent.Name,
                    AgentIcon = agent.Icon,
                    DocumentCount = agent.Documents.Count
                });
            }

            // Load assignments for each channel
            foreach (var channel in Channels.Where(c => !c.IsComingSoon))
            {
                await LoadChannelAssignmentsAsync(channel);
            }

            _debugService?.Info("ChannelsVM", $"Loaded {agents.Count} agents for assignment");
        }
        catch (Exception ex)
        {
            _debugService?.Error("ChannelsVM", "Failed to load agents", ex.Message);
        }
    }

    private async Task LoadChannelAssignmentsAsync(ChannelConfigViewModel channel)
    {
        if (_agentService == null) return;

        try
        {
            var assignments = await _agentService.GetChannelAssignmentsAsync(channel.Type);
            var assignedIds = assignments.Select(a => a.AgentId).ToHashSet();

            channel.AssignedAgents.Clear();
            foreach (var assignment in assignments)
            {
                if (assignment.Agent != null)
                {
                    channel.AssignedAgents.Add(new AgentAssignmentViewModel
                    {
                        AgentId = assignment.AgentId,
                        AgentName = assignment.Agent.Name,
                        AgentIcon = assignment.Agent.Icon,
                        DocumentCount = assignment.Agent.Documents.Count,
                        IsAssigned = true,
                        Priority = assignment.Priority
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _debugService?.Error("ChannelsVM", $"Failed to load assignments for {channel.Name}", ex.Message);
        }
    }

    [RelayCommand]
    private async Task AssignAgent(AgentAssignmentViewModel agent)
    {
        if (_agentService == null || SelectedChannel == null) return;

        try
        {
            var priority = SelectedChannel.AssignedAgents.Count;
            await _agentService.AssignAgentToChannelAsync(agent.AgentId, SelectedChannel.Type, priority);

            agent.IsAssigned = true;
            agent.Priority = priority;
            SelectedChannel.AssignedAgents.Add(agent);

            StatusMessage = $"Assigned '{agent.AgentName}' to {SelectedChannel.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UnassignAgent(AgentAssignmentViewModel agent)
    {
        if (_agentService == null || SelectedChannel == null) return;

        try
        {
            await _agentService.UnassignAgentFromChannelAsync(agent.AgentId, SelectedChannel.Type);

            agent.IsAssigned = false;
            SelectedChannel.AssignedAgents.Remove(agent);

            StatusMessage = $"Removed '{agent.AgentName}' from {SelectedChannel.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void OnQrCodeGenerated(object? sender, string qrCodeBase64)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            try
            {
                var base64Data = qrCodeBase64.Replace("data:image/png;base64,", "");
                var imageBytes = Convert.FromBase64String(base64Data);

                using var ms = new System.IO.MemoryStream(imageBytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();

                QrCodeImage = image;
                ShowQrCode = true;
                WhatsAppStatus = "Scan QR code with WhatsApp";
                StatusMessage = "Scan the QR code with your WhatsApp app";

                _debugService?.Info("WhatsApp", "QR code displayed");
            }
            catch (Exception ex)
            {
                _debugService?.Error("WhatsApp", "Failed to display QR code", ex.Message);
            }
        });
    }

    private void OnWhatsAppConnectionStatusChanged(object? sender, WhatsAppConnectionStatus status)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var whatsAppChannel = Channels.FirstOrDefault(c => c.Type == ChannelType.WhatsApp);
            if (whatsAppChannel == null) return;

            switch (status.Status)
            {
                case "connected":
                    whatsAppChannel.IsConnected = true;
                    whatsAppChannel.IsConnecting = false;
                    ShowQrCode = false;
                    QrCodeImage = null;
                    ConnectedNumber = status.ConnectedNumber;
                    WhatsAppStatus = $"Connected: {status.ConnectedNumber}";
                    StatusMessage = $"WhatsApp connected as {status.ConnectedNumber}";
                    _debugService?.Success("WhatsApp", $"Connected as {status.ConnectedNumber}");
                    break;

                case "waiting_for_scan":
                    whatsAppChannel.IsConnecting = true;
                    WhatsAppStatus = "Waiting for QR scan...";
                    break;

                case "disconnected":
                    whatsAppChannel.IsConnected = false;
                    whatsAppChannel.IsConnecting = false;
                    ShowQrCode = false;
                    QrCodeImage = null;
                    ConnectedNumber = null;
                    WhatsAppStatus = "Disconnected";
                    StatusMessage = "WhatsApp disconnected";
                    break;

                default:
                    WhatsAppStatus = status.Status;
                    break;
            }
        });
    }

    private void OnTelegramConnectionStatusChanged(object? sender, TelegramConnectionStatus status)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var telegramChannel = Channels.FirstOrDefault(c => c.Type == ChannelType.Telegram);
            if (telegramChannel == null) return;

            telegramChannel.IsConnected = status.IsConnected;
            telegramChannel.IsConnecting = false;

            if (status.IsConnected)
            {
                TelegramStatus = $"Connected: @{status.BotUsername}";
                StatusMessage = $"Telegram connected as @{status.BotUsername}";
                _debugService?.Success("Telegram", $"Connected as @{status.BotUsername}");
            }
            else
            {
                TelegramStatus = "Disconnected";
                StatusMessage = "Telegram disconnected";
            }
        });
    }

    private void OnDiscordConnectionStatusChanged(object? sender, DiscordConnectionStatus status)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var discordChannel = Channels.FirstOrDefault(c => c.Type == ChannelType.Discord);
            if (discordChannel == null) return;

            discordChannel.IsConnected = status.IsConnected;
            discordChannel.IsConnecting = false;

            if (status.IsConnected)
            {
                DiscordStatus = $"Connected: {status.BotUsername}";
                StatusMessage = $"Discord connected as {status.BotUsername}";
                _debugService?.Success("Discord", $"Connected as {status.BotUsername}");
            }
            else
            {
                DiscordStatus = "Disconnected";
                StatusMessage = "Discord disconnected";
            }
        });
    }

    private void OnSlackConnectionStatusChanged(object? sender, SlackConnectionStatus status)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var slackChannel = Channels.FirstOrDefault(c => c.Type == ChannelType.Slack);
            if (slackChannel == null) return;

            slackChannel.IsConnected = status.IsConnected;
            slackChannel.IsConnecting = false;

            if (status.IsConnected)
            {
                SlackStatus = $"Connected: @{status.BotUsername}";
                StatusMessage = $"Slack connected as @{status.BotUsername}";
                _debugService?.Success("Slack", $"Connected as @{status.BotUsername}");
            }
            else
            {
                SlackStatus = "Disconnected";
                StatusMessage = "Slack disconnected";
            }
        });
    }

    private void OnMessageReceived(object? sender, ChannelMessage message)
    {
        _debugService?.Info("WhatsApp", $"Message received from {message.SenderName}", message.Content);
    }

    [RelayCommand]
    private async Task ConnectChannel(ChannelConfigViewModel channel)
    {
        // Validate required fields
        var missingFields = channel.ConfigurationFields
            .Where(f => f.IsRequired && string.IsNullOrWhiteSpace(f.Value))
            .Select(f => f.Label)
            .ToList();

        if (missingFields.Count > 0 && channel.Type != ChannelType.WhatsApp)
        {
            StatusMessage = $"Missing required fields: {string.Join(", ", missingFields)}";
            return;
        }

        channel.IsConnecting = true;
        StatusMessage = $"Connecting to {channel.Name}...";

        try
        {
            var config = channel.ConfigurationFields
                .ToDictionary(f => f.Key, f => f.Value);

            switch (channel.Type)
            {
                case ChannelType.WhatsApp:
                    await ConnectWhatsAppAsync(channel);
                    break;

                case ChannelType.Telegram:
                    await ConnectTelegramAsync(channel, config);
                    break;

                case ChannelType.Discord:
                    await ConnectDiscordAsync(channel, config);
                    break;

                case ChannelType.Slack:
                    await ConnectSlackAsync(channel, config);
                    break;

                default:
                    StatusMessage = $"{channel.Name} is not yet supported";
                    channel.IsConnecting = false;
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            channel.IsConnected = false;
            channel.IsConnecting = false;
            _debugService?.Error(channel.Name, "Connection failed", ex.Message);
        }
    }

    private async Task ConnectTelegramAsync(ChannelConfigViewModel channel, Dictionary<string, string> config)
    {
        if (_telegramChannel == null)
        {
            StatusMessage = "Telegram channel not available";
            channel.IsConnecting = false;
            return;
        }

        TelegramStatus = "Connecting...";
        await _telegramChannel.ConnectAsync(config);
    }

    private async Task ConnectDiscordAsync(ChannelConfigViewModel channel, Dictionary<string, string> config)
    {
        if (_discordChannel == null)
        {
            StatusMessage = "Discord channel not available";
            channel.IsConnecting = false;
            return;
        }

        DiscordStatus = "Connecting...";
        await _discordChannel.ConnectAsync(config);
    }

    private async Task ConnectSlackAsync(ChannelConfigViewModel channel, Dictionary<string, string> config)
    {
        if (_slackChannel == null)
        {
            StatusMessage = "Slack channel not available";
            channel.IsConnecting = false;
            return;
        }

        SlackStatus = "Connecting...";
        await _slackChannel.ConnectAsync(config);
    }

    private async Task ConnectWhatsAppAsync(ChannelConfigViewModel channel)
    {
        if (_whatsAppChannel == null)
        {
            StatusMessage = "WhatsApp channel not available";
            return;
        }

        channel.IsConnecting = true;
        StatusMessage = "Starting WhatsApp connection...";
        WhatsAppStatus = "Initializing...";

        try
        {
            await _whatsAppChannel.ConnectAsync(new Dictionary<string, string>());
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            WhatsAppStatus = "Connection failed";
            channel.IsConnecting = false;
            ShowQrCode = false;

            _debugService?.Error("WhatsApp", "Connection failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DisconnectChannel(ChannelConfigViewModel channel)
    {
        try
        {
            switch (channel.Type)
            {
                case ChannelType.WhatsApp when _whatsAppChannel != null:
                    await _whatsAppChannel.DisconnectAsync();
                    ShowQrCode = false;
                    QrCodeImage = null;
                    ConnectedNumber = null;
                    WhatsAppStatus = "Disconnected";
                    break;

                case ChannelType.Telegram when _telegramChannel != null:
                    await _telegramChannel.DisconnectAsync();
                    TelegramStatus = "Disconnected";
                    break;

                case ChannelType.Discord when _discordChannel != null:
                    await _discordChannel.DisconnectAsync();
                    DiscordStatus = "Disconnected";
                    break;

                case ChannelType.Slack when _slackChannel != null:
                    await _slackChannel.DisconnectAsync();
                    SlackStatus = "Disconnected";
                    break;

                default:
                    if (_channelManager != null)
                        await _channelManager.DisconnectChannelAsync(channel.Type);
                    break;
            }

            channel.IsConnected = false;
            channel.IsConnecting = false;
            StatusMessage = $"Disconnected from {channel.Name}";
            _debugService?.Info(channel.Name, "Disconnected");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error disconnecting: {ex.Message}";
            _debugService?.Error(channel.Name, "Disconnect failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveChannelConfig(ChannelConfigViewModel channel)
    {
        var config = channel.ConfigurationFields
            .ToDictionary(f => f.Key, f => (object)f.Value);

        await _settingsService.SetAsync($"channel_{channel.Type}", config);
        StatusMessage = $"{channel.Name} configuration saved";
    }

    [RelayCommand]
    private void RefreshQrCode()
    {
        if (_whatsAppChannel != null)
        {
            StatusMessage = "Refreshing QR code...";
            _ = _whatsAppChannel.ConnectAsync(new Dictionary<string, string>());
        }
    }
}

public partial class ChannelConfigViewModel : ObservableObject
{
    public ChannelType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsComingSoon { get; set; }
    public bool UsesQrCode { get; set; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private ObservableCollection<ConfigFieldViewModel> _configurationFields = [];

    [ObservableProperty]
    private ObservableCollection<AgentAssignmentViewModel> _assignedAgents = [];
}

public partial class ConfigFieldViewModel : ObservableObject
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsPassword { get; set; }

    [ObservableProperty]
    private string _value = string.Empty;
}

public partial class AgentAssignmentViewModel : ObservableObject
{
    public int AgentId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentIcon { get; set; } = "Robot";
    public int DocumentCount { get; set; }

    [ObservableProperty]
    private bool _isAssigned;

    [ObservableProperty]
    private int _priority;
}
