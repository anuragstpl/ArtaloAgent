using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArtaloBot.App.ViewModels;

public partial class MCPViewModel : ObservableObject
{
    private readonly IMCPService _mcpService;
    private readonly IDebugService _debugService;

    [ObservableProperty]
    private ObservableCollection<MCPServerViewModel> _servers = [];

    [ObservableProperty]
    private MCPServerViewModel? _selectedServer;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isAddingNew;

    [ObservableProperty]
    private MCPServerViewModel? _editingServer;

    public MCPViewModel(IMCPService mcpService, IDebugService debugService)
    {
        _mcpService = mcpService;
        _debugService = debugService;

        // Subscribe to state changes
        _mcpService.ServerStateChanged += OnServerStateChanged;
    }

    public async Task LoadServersAsync()
    {
        IsLoading = true;
        try
        {
            var configs = await _mcpService.GetServersAsync();
            Servers.Clear();

            foreach (var config in configs)
            {
                var state = _mcpService.GetServerState(config.Id);
                var vm = new MCPServerViewModel(config, state);
                Servers.Add(vm);
            }

            StatusMessage = $"Loaded {Servers.Count} MCP server(s)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading servers: {ex.Message}";
            _debugService.Error("MCP", "Failed to load servers", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnServerStateChanged(object? sender, MCPServerState state)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var server = Servers.FirstOrDefault(s => s.Config.Id == state.ConfigId);
            if (server != null)
            {
                server.UpdateState(state);
            }
        });
    }

    [RelayCommand]
    private void StartAddNew()
    {
        EditingServer = new MCPServerViewModel(new MCPServerConfig
        {
            Name = "New MCP Server",
            ServerType = "stdio",
            Command = "npx",
            Arguments = "[]",
            EnvironmentVariables = "{}"
        }, null);
        IsAddingNew = true;
    }

    [RelayCommand]
    private void StartEdit(MCPServerViewModel server)
    {
        // Clone the config for editing
        var clonedConfig = new MCPServerConfig
        {
            Id = server.Config.Id,
            Name = server.Config.Name,
            Description = server.Config.Description,
            ServerType = server.Config.ServerType,
            Command = server.Config.Command,
            Arguments = server.Config.Arguments,
            WorkingDirectory = server.Config.WorkingDirectory,
            EnvironmentVariables = server.Config.EnvironmentVariables,
            Url = server.Config.Url,
            IsEnabled = server.Config.IsEnabled,
            AutoStart = server.Config.AutoStart
        };
        EditingServer = new MCPServerViewModel(clonedConfig, server.State);
        IsAddingNew = false;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingServer = null;
        IsAddingNew = false;
    }

    [RelayCommand]
    private async Task SaveServer()
    {
        if (EditingServer == null) return;

        try
        {
            IsLoading = true;

            if (IsAddingNew)
            {
                var newConfig = await _mcpService.AddServerAsync(EditingServer.Config);
                EditingServer.Config.Id = newConfig.Id;
                Servers.Add(EditingServer);
                StatusMessage = $"Added server: {EditingServer.Name}";
            }
            else
            {
                await _mcpService.UpdateServerAsync(EditingServer.Config);

                // Update the existing item in the list
                var existing = Servers.FirstOrDefault(s => s.Config.Id == EditingServer.Config.Id);
                if (existing != null)
                {
                    var index = Servers.IndexOf(existing);
                    Servers[index] = EditingServer;
                }

                StatusMessage = $"Updated server: {EditingServer.Name}";
            }

            EditingServer = null;
            IsAddingNew = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving server: {ex.Message}";
            _debugService.Error("MCP", "Failed to save server", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteServer(MCPServerViewModel server)
    {
        try
        {
            await _mcpService.DeleteServerAsync(server.Config.Id);
            Servers.Remove(server);
            StatusMessage = $"Deleted server: {server.Name}";

            if (SelectedServer == server)
            {
                SelectedServer = null;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting server: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Connect(MCPServerViewModel server)
    {
        try
        {
            server.IsConnecting = true;
            StatusMessage = $"Connecting to {server.Name}...";

            var state = await _mcpService.ConnectAsync(server.Config.Id);
            server.UpdateState(state);

            StatusMessage = $"Connected to {server.Name} - {state.Tools.Count} tools available";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to connect: {ex.Message}";
        }
        finally
        {
            server.IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task Disconnect(MCPServerViewModel server)
    {
        try
        {
            await _mcpService.DisconnectAsync(server.Config.Id);
            StatusMessage = $"Disconnected from {server.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error disconnecting: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestTool(MCPToolViewModel tool)
    {
        if (SelectedServer == null) return;

        try
        {
            StatusMessage = $"Calling tool: {tool.Name}...";

            // Parse test arguments
            Dictionary<string, object>? args = null;
            if (!string.IsNullOrEmpty(tool.TestArguments))
            {
                args = JsonSerializer.Deserialize<Dictionary<string, object>>(tool.TestArguments);
            }

            var result = await _mcpService.CallToolAsync(
                SelectedServer.Config.Id,
                tool.Name,
                args);

            tool.TestResult = result.Success
                ? result.Content ?? "Success (no content)"
                : $"Error: {result.Error}";

            StatusMessage = result.Success
                ? $"Tool {tool.Name} executed successfully"
                : $"Tool {tool.Name} failed: {result.Error}";
        }
        catch (Exception ex)
        {
            tool.TestResult = $"Error: {ex.Message}";
            StatusMessage = $"Error calling tool: {ex.Message}";
        }
    }
}

public partial class MCPServerViewModel : ObservableObject
{
    public MCPServerConfig Config { get; }

    [ObservableProperty]
    private MCPServerState? _state;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private ObservableCollection<MCPToolViewModel> _tools = [];

    public MCPServerViewModel(MCPServerConfig config, MCPServerState? state)
    {
        Config = config;
        State = state;
        UpdateTools();
    }

    public string Name
    {
        get => Config.Name;
        set { Config.Name = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => Config.Description;
        set { Config.Description = value; OnPropertyChanged(); }
    }

    public string Command
    {
        get => Config.Command;
        set { Config.Command = value; OnPropertyChanged(); }
    }

    public string Arguments
    {
        get => Config.Arguments;
        set { Config.Arguments = value; OnPropertyChanged(); }
    }

    public string WorkingDirectory
    {
        get => Config.WorkingDirectory;
        set { Config.WorkingDirectory = value; OnPropertyChanged(); }
    }

    public string EnvironmentVariables
    {
        get => Config.EnvironmentVariables;
        set { Config.EnvironmentVariables = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => Config.IsEnabled;
        set { Config.IsEnabled = value; OnPropertyChanged(); }
    }

    public bool AutoStart
    {
        get => Config.AutoStart;
        set { Config.AutoStart = value; OnPropertyChanged(); }
    }

    public bool IsConnected => State?.Status == MCPServerStatus.Connected;
    public bool HasError => State?.Status == MCPServerStatus.Error;
    public string? ErrorMessage => State?.ErrorMessage;

    public string StatusText => State?.Status switch
    {
        MCPServerStatus.Connected => $"Connected ({Tools.Count} tools)",
        MCPServerStatus.Connecting => "Connecting...",
        MCPServerStatus.Error => $"Error: {State?.ErrorMessage}",
        _ => "Disconnected"
    };

    public string StatusColor => State?.Status switch
    {
        MCPServerStatus.Connected => "#107C10",
        MCPServerStatus.Connecting => "#F7B500",
        MCPServerStatus.Error => "#D13438",
        _ => "#666666"
    };

    public void UpdateState(MCPServerState state)
    {
        State = state;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        UpdateTools();
    }

    private void UpdateTools()
    {
        Tools.Clear();
        if (State?.Tools != null)
        {
            foreach (var tool in State.Tools)
            {
                Tools.Add(new MCPToolViewModel(tool));
            }
        }
    }
}

public partial class MCPToolViewModel : ObservableObject
{
    public MCPTool Tool { get; }

    public MCPToolViewModel(MCPTool tool)
    {
        Tool = tool;
        GenerateDefaultTestArgs();
    }

    public string Name => Tool.Name;
    public string Description => Tool.Description;

    public string InputSchemaDescription
    {
        get
        {
            if (Tool.InputSchema?.Properties == null) return "No parameters";

            var required = Tool.InputSchema.Required ?? [];
            var props = Tool.InputSchema.Properties
                .Select(p => $"{p.Key}: {p.Value.Type}" + (required.Contains(p.Key) ? " (required)" : ""))
                .ToList();

            return string.Join(", ", props);
        }
    }

    [ObservableProperty]
    private string _testArguments = "{}";

    [ObservableProperty]
    private string? _testResult;

    private void GenerateDefaultTestArgs()
    {
        if (Tool.InputSchema?.Properties == null)
        {
            TestArguments = "{}";
            return;
        }

        var args = new Dictionary<string, object>();
        foreach (var prop in Tool.InputSchema.Properties)
        {
            args[prop.Key] = prop.Value.Type switch
            {
                "string" => "",
                "number" => 0,
                "integer" => 0,
                "boolean" => false,
                _ => ""
            };
        }

        TestArguments = JsonSerializer.Serialize(args, new JsonSerializerOptions { WriteIndented = true });
    }
}
