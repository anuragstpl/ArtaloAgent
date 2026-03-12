using System.Collections.ObjectModel;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArtaloBot.App.ViewModels;

public partial class ChannelsViewModel : ObservableObject
{
    private readonly IChannelManager? _channelManager;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private ObservableCollection<ChannelConfigViewModel> _channels = [];

    [ObservableProperty]
    private ChannelConfigViewModel? _selectedChannel;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ChannelsViewModel(ISettingsService settingsService, IChannelManager? channelManager = null)
    {
        _settingsService = settingsService;
        _channelManager = channelManager;

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
                Description = "Connect via Twilio WhatsApp API",
                ConfigurationFields =
                [
                    new ConfigFieldViewModel { Key = "AccountSid", Label = "Twilio Account SID", IsRequired = true },
                    new ConfigFieldViewModel { Key = "AuthToken", Label = "Twilio Auth Token", IsRequired = true, IsPassword = true },
                    new ConfigFieldViewModel { Key = "FromNumber", Label = "WhatsApp Number", IsRequired = true, Placeholder = "+1234567890" }
                ]
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
            }
        ];

        SelectedChannel = Channels.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ConnectChannel(ChannelConfigViewModel channel)
    {
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

    [RelayCommand]
    private async Task DisconnectChannel(ChannelConfigViewModel channel)
    {
        if (_channelManager == null) return;

        await _channelManager.DisconnectChannelAsync(channel.Type);
        channel.IsConnected = false;
        StatusMessage = $"Disconnected from {channel.Name}";
    }

    [RelayCommand]
    private async Task SaveChannelConfig(ChannelConfigViewModel channel)
    {
        // Save configuration to settings
        var config = channel.ConfigurationFields
            .ToDictionary(f => f.Key, f => (object)f.Value);

        await _settingsService.SetAsync($"channel_{channel.Type}", config);
        StatusMessage = $"{channel.Name} configuration saved";
    }
}

public partial class ChannelConfigViewModel : ObservableObject
{
    public ChannelType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsComingSoon { get; set; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private ObservableCollection<ConfigFieldViewModel> _configurationFields = [];
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
