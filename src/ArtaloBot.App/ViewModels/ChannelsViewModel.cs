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

        // Get WhatsApp channel from manager
        _whatsAppChannel = channelManager?.GetProvider(ChannelType.WhatsApp) as WhatsAppChannel;

        if (_whatsAppChannel != null)
        {
            _whatsAppChannel.QrCodeGenerated += OnQrCodeGenerated;
            _whatsAppChannel.ConnectionStatusChanged += OnConnectionStatusChanged;
            _whatsAppChannel.MessageReceived += OnMessageReceived;
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
                Description = "Connect via Telegram Bot API",
                IsComingSoon = true,
                ConfigurationFields =
                [
                    new ConfigFieldViewModel { Key = "BotToken", Label = "Bot Token", IsRequired = true, IsPassword = true }
                ]
            },
            new ChannelConfigViewModel
            {
                Type = ChannelType.Slack,
                Name = "Slack",
                Icon = "Slack",
                Description = "Connect to Slack workspace",
                IsComingSoon = true,
                ConfigurationFields =
                [
                    new ConfigFieldViewModel { Key = "BotToken", Label = "Bot Token", IsRequired = true, IsPassword = true },
                    new ConfigFieldViewModel { Key = "AppToken", Label = "App Token", IsRequired = true, IsPassword = true }
                ]
            },
            new ChannelConfigViewModel
            {
                Type = ChannelType.Discord,
                Name = "Discord",
                Icon = "Discord",
                Description = "Connect via Discord Bot",
                IsComingSoon = true,
                ConfigurationFields =
                [
                    new ConfigFieldViewModel { Key = "BotToken", Label = "Bot Token", IsRequired = true, IsPassword = true },
                    new ConfigFieldViewModel { Key = "GuildId", Label = "Server ID", IsRequired = false }
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

    private void OnConnectionStatusChanged(object? sender, WhatsAppConnectionStatus status)
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

    private void OnMessageReceived(object? sender, ChannelMessage message)
    {
        _debugService?.Info("WhatsApp", $"Message received from {message.SenderName}", message.Content);
    }

    [RelayCommand]
    private async Task ConnectChannel(ChannelConfigViewModel channel)
    {
        if (channel.Type == ChannelType.WhatsApp)
        {
            await ConnectWhatsAppAsync(channel);
            return;
        }

        if (_channelManager == null)
        {
            StatusMessage = "Channel manager not available";
            return;
        }

        channel.IsConnecting = true;
        StatusMessage = $"Connecting to {channel.Name}...";

        try
        {
            var config = channel.ConfigurationFields
                .ToDictionary(f => f.Key, f => f.Value);

            var success = await _channelManager.ConnectChannelAsync(channel.Type, config);

            channel.IsConnected = success;
            StatusMessage = success
                ? $"Connected to {channel.Name} successfully!"
                : $"Failed to connect to {channel.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            channel.IsConnected = false;
        }
        finally
        {
            channel.IsConnecting = false;
        }
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
        if (channel.Type == ChannelType.WhatsApp && _whatsAppChannel != null)
        {
            await _whatsAppChannel.DisconnectAsync();
            channel.IsConnected = false;
            ShowQrCode = false;
            QrCodeImage = null;
            ConnectedNumber = null;
            WhatsAppStatus = "Disconnected";
            StatusMessage = "WhatsApp disconnected";
            return;
        }

        if (_channelManager == null) return;

        await _channelManager.DisconnectChannelAsync(channel.Type);
        channel.IsConnected = false;
        StatusMessage = $"Disconnected from {channel.Name}";
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
