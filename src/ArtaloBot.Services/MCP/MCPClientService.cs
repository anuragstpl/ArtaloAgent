using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using ArtaloBot.Data;
using Microsoft.EntityFrameworkCore;

namespace ArtaloBot.Services.MCP;

/// <summary>
/// Service for managing MCP server connections and tool calls.
/// Supports stdio-based MCP servers.
/// </summary>
public class MCPClientService : IMCPService, IDisposable
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDebugService? _debugService;
    private readonly ConcurrentDictionary<int, MCPServerConnection> _connections = new();
    private readonly ConcurrentDictionary<int, MCPServerState> _states = new();
    private readonly ConcurrentDictionary<int, MCPServerConfig> _configCache = new();
    private int _requestId = 0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public event EventHandler<MCPServerState>? ServerStateChanged;

    public MCPClientService(IDbContextFactory<AppDbContext> dbFactory, IDebugService? debugService = null)
    {
        _dbFactory = dbFactory;
        _debugService = debugService;
    }

    #region Server Configuration CRUD

    public async Task<IReadOnlyList<MCPServerConfig>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.MCPServers.OrderBy(s => s.Name).ToListAsync(cancellationToken);
    }

    public async Task<MCPServerConfig?> GetServerAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.MCPServers.FindAsync([id], cancellationToken);
    }

    public async Task<MCPServerConfig> AddServerAsync(MCPServerConfig config, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        config.CreatedAt = DateTime.UtcNow;
        config.UpdatedAt = DateTime.UtcNow;
        db.MCPServers.Add(config);
        await db.SaveChangesAsync(cancellationToken);

        _debugService?.Success("MCP", $"Added server: {config.Name}", $"ID: {config.Id}, Command: {config.Command}");
        return config;
    }

    public async Task UpdateServerAsync(MCPServerConfig config, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        config.UpdatedAt = DateTime.UtcNow;
        db.MCPServers.Update(config);
        await db.SaveChangesAsync(cancellationToken);

        _debugService?.Info("MCP", $"Updated server: {config.Name}");
    }

    public async Task DeleteServerAsync(int id, CancellationToken cancellationToken = default)
    {
        // Disconnect first if connected
        await DisconnectAsync(id, cancellationToken);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var config = await db.MCPServers.FindAsync([id], cancellationToken);
        if (config != null)
        {
            db.MCPServers.Remove(config);
            await db.SaveChangesAsync(cancellationToken);
            _debugService?.Info("MCP", $"Deleted server: {config.Name}");
        }
    }

    #endregion

    #region Connection Management

    public async Task<MCPServerState> ConnectAsync(int serverId, CancellationToken cancellationToken = default)
    {
        var config = await GetServerAsync(serverId, cancellationToken);
        if (config == null)
        {
            throw new InvalidOperationException($"Server {serverId} not found");
        }

        // Cache the config for synchronous access later
        _configCache[serverId] = config;

        // Update state to connecting
        var state = new MCPServerState
        {
            ConfigId = serverId,
            Status = MCPServerStatus.Connecting
        };
        _states[serverId] = state;
        ServerStateChanged?.Invoke(this, state);

        _debugService?.Info("MCP", $"Connecting to server: {config.Name}",
            $"Command: {config.Command} {config.Arguments}");

        try
        {
            // Start the process
            var connection = await StartServerProcessAsync(config, cancellationToken);
            _connections[serverId] = connection;

            // Initialize the MCP protocol
            await InitializeProtocolAsync(connection, cancellationToken);

            // Get available tools
            var tools = await ListToolsAsync(connection, cancellationToken);

            // Update state
            state.Status = MCPServerStatus.Connected;
            state.Tools = tools;
            state.LastConnected = DateTime.UtcNow;
            state.ErrorMessage = null;

            // Cache tools in config
            config.CachedTools = JsonSerializer.Serialize(tools, JsonOptions);
            await UpdateServerAsync(config, cancellationToken);

            _debugService?.Success("MCP", $"Connected to {config.Name}",
                $"Tools available: {string.Join(", ", tools.Select(t => t.Name))}");

            ServerStateChanged?.Invoke(this, state);
            return state;
        }
        catch (Exception ex)
        {
            state.Status = MCPServerStatus.Error;
            state.ErrorMessage = ex.Message;

            _debugService?.Error("MCP", $"Failed to connect to {config.Name}", ex.Message);

            ServerStateChanged?.Invoke(this, state);
            throw;
        }
    }

    public async Task DisconnectAsync(int serverId, CancellationToken cancellationToken = default)
    {
        if (_connections.TryRemove(serverId, out var connection))
        {
            try
            {
                connection.Process?.Kill();
                connection.Process?.Dispose();
            }
            catch { /* Ignore errors during cleanup */ }

            _debugService?.Info("MCP", $"Disconnected from server {serverId}");
        }

        // Keep the config in cache for potential reconnection
        // _configCache.TryRemove(serverId, out _);

        if (_states.TryGetValue(serverId, out var state))
        {
            state.Status = MCPServerStatus.Disconnected;
            state.ErrorMessage = null;
            state.Tools.Clear();
            ServerStateChanged?.Invoke(this, state);
        }

        await Task.CompletedTask; // Satisfy async signature
    }

    public MCPServerState? GetServerState(int serverId)
    {
        return _states.TryGetValue(serverId, out var state) ? state : null;
    }

    public IReadOnlyList<MCPServerState> GetAllServerStates()
    {
        return _states.Values.ToList();
    }

    public IReadOnlyList<(MCPServerConfig Server, MCPTool Tool)> GetAllAvailableTools()
    {
        var result = new List<(MCPServerConfig, MCPTool)>();

        foreach (var state in _states.Values)
        {
            if (state.Status != MCPServerStatus.Connected) continue;

            // Use cached config to avoid async deadlock
            if (!_configCache.TryGetValue(state.ConfigId, out var config) || config == null)
                continue;

            foreach (var tool in state.Tools)
            {
                result.Add((config, tool));
            }
        }

        return result;
    }

    #endregion

    #region Tool Calls

    public async Task<MCPToolResult> CallToolAsync(
        int serverId,
        string toolName,
        Dictionary<string, object>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(serverId, out var connection))
        {
            return new MCPToolResult
            {
                Success = false,
                IsError = true,
                Error = $"Server {serverId} is not connected"
            };
        }

        _debugService?.Info("MCP", $"Calling tool: {toolName}",
            $"Server: {serverId}, Arguments: {JsonSerializer.Serialize(arguments)}");

        try
        {
            var request = new JsonRpcRequest
            {
                Id = Interlocked.Increment(ref _requestId),
                Method = "tools/call",
                Params = new MCPToolCallParams
                {
                    Name = toolName,
                    Arguments = arguments
                }
            };

            var response = await SendRequestAsync<MCPToolCallResult>(connection, request, cancellationToken);

            var content = response?.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var isError = response?.IsError ?? false;

            _debugService?.Success("MCP", $"Tool {toolName} completed",
                $"IsError: {isError}, Content: {content.Substring(0, Math.Min(200, content.Length))}...");

            return new MCPToolResult
            {
                Success = !isError,
                Content = content,
                IsError = isError
            };
        }
        catch (Exception ex)
        {
            _debugService?.Error("MCP", $"Tool {toolName} failed", ex.Message);

            return new MCPToolResult
            {
                Success = false,
                IsError = true,
                Error = ex.Message
            };
        }
    }

    public async Task<MCPToolResult> CallToolByNameAsync(
        string toolName,
        Dictionary<string, object>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        // Find the server that has this tool
        foreach (var state in _states.Values)
        {
            if (state.Status != MCPServerStatus.Connected) continue;

            var tool = state.Tools.FirstOrDefault(t =>
                t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));

            if (tool != null)
            {
                return await CallToolAsync(state.ConfigId, toolName, arguments, cancellationToken);
            }
        }

        return new MCPToolResult
        {
            Success = false,
            IsError = true,
            Error = $"Tool '{toolName}' not found on any connected server"
        };
    }

    #endregion

    #region Private Methods

    private async Task<MCPServerConnection> StartServerProcessAsync(
        MCPServerConfig config,
        CancellationToken cancellationToken)
    {
        // Safely parse arguments - handle null, empty, or invalid JSON
        List<string> arguments = [];
        if (!string.IsNullOrWhiteSpace(config.Arguments))
        {
            try
            {
                arguments = JsonSerializer.Deserialize<List<string>>(config.Arguments) ?? [];
            }
            catch (JsonException ex)
            {
                _debugService?.Warning("MCP", $"Invalid arguments JSON: {ex.Message}", config.Arguments);
            }
        }

        // Safely parse environment variables
        Dictionary<string, string> envVars = [];
        if (!string.IsNullOrWhiteSpace(config.EnvironmentVariables))
        {
            try
            {
                envVars = JsonSerializer.Deserialize<Dictionary<string, string>>(config.EnvironmentVariables) ?? [];
            }
            catch (JsonException ex)
            {
                _debugService?.Warning("MCP", $"Invalid environment variables JSON: {ex.Message}", config.EnvironmentVariables);
            }
        }

        if (string.IsNullOrWhiteSpace(config.Command))
        {
            throw new InvalidOperationException("Server command is not configured");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = config.Command,
            Arguments = string.Join(" ", arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrEmpty(config.WorkingDirectory)
                ? Environment.CurrentDirectory
                : config.WorkingDirectory
        };

        foreach (var (key, value) in envVars)
        {
            if (!string.IsNullOrEmpty(key))
            {
                startInfo.EnvironmentVariables[key] = value ?? string.Empty;
            }
        }

        _debugService?.Info("MCP", $"Starting process: {config.Command}",
            $"Arguments: {startInfo.Arguments}\nWorkingDir: {startInfo.WorkingDirectory}");

        var process = new Process { StartInfo = startInfo };
        var stderrBuilder = new StringBuilder();

        // Start error logging
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                stderrBuilder.AppendLine(e.Data);
                _debugService?.Warning("MCP", $"Server stderr: {e.Data}");
            }
        };

        try
        {
            process.Start();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to start process '{config.Command}': {ex.Message}", ex);
        }

        // Give the process a moment to start
        await Task.Delay(500, cancellationToken);

        if (process.HasExited)
        {
            var error = stderrBuilder.ToString();
            throw new InvalidOperationException($"Process exited immediately with code {process.ExitCode}: {error}");
        }

        return new MCPServerConnection
        {
            ConfigId = config.Id,
            Process = process,
            Input = process.StandardInput,
            Output = process.StandardOutput
        };
    }

    private async Task InitializeProtocolAsync(MCPServerConnection connection, CancellationToken cancellationToken)
    {
        var initRequest = new JsonRpcRequest
        {
            Id = Interlocked.Increment(ref _requestId),
            Method = "initialize",
            Params = new MCPInitializeParams()
        };

        await SendRequestAsync<object>(connection, initRequest, cancellationToken);

        // Send initialized notification
        var initializedNotification = new JsonRpcRequest
        {
            Id = 0, // Notifications don't have an ID
            Method = "notifications/initialized"
        };

        await SendNotificationAsync(connection, initializedNotification, cancellationToken);
    }

    private async Task<List<MCPTool>> ListToolsAsync(MCPServerConnection connection, CancellationToken cancellationToken)
    {
        var request = new JsonRpcRequest
        {
            Id = Interlocked.Increment(ref _requestId),
            Method = "tools/list",
            Params = new { }
        };

        var result = await SendRequestAsync<MCPToolsListResult>(connection, request, cancellationToken);
        return result?.Tools ?? [];
    }

    private async Task<T?> SendRequestAsync<T>(
        MCPServerConnection connection,
        JsonRpcRequest request,
        CancellationToken cancellationToken)
    {
        if (connection?.Input == null || connection?.Output == null)
        {
            throw new InvalidOperationException("Connection streams are not available");
        }

        var json = JsonSerializer.Serialize(request, JsonOptions);

        _debugService?.Info("MCP", $"Sending: {request.Method}", json);

        try
        {
            await connection.Input.WriteLineAsync(json);
            await connection.Input.FlushAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to send request: {ex.Message}", ex);
        }

        // Read response with timeout
        string? responseLine;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout

            responseLine = await connection.Output.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("MCP server did not respond within timeout");
        }

        if (string.IsNullOrEmpty(responseLine))
        {
            throw new InvalidOperationException("Empty response from MCP server");
        }

        _debugService?.Info("MCP", "Received response", responseLine.Length > 500 ? responseLine[..500] : responseLine);

        JsonRpcResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<JsonRpcResponse>(responseLine, JsonOptions);
        }
        catch (JsonException ex)
        {
            _debugService?.Error("MCP", $"Failed to parse response: {ex.Message}", responseLine);
            throw new InvalidOperationException($"Invalid JSON response from MCP server: {ex.Message}");
        }

        if (response?.Error != null)
        {
            throw new InvalidOperationException($"MCP error: {response.Error.Message}");
        }

        if (response?.Result == null)
        {
            return default;
        }

        // Deserialize result to target type
        try
        {
            var resultJson = JsonSerializer.Serialize(response.Result, JsonOptions);
            return JsonSerializer.Deserialize<T>(resultJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            _debugService?.Error("MCP", $"Failed to deserialize result: {ex.Message}");
            return default;
        }
    }

    private async Task SendNotificationAsync(
        MCPServerConnection connection,
        JsonRpcRequest notification,
        CancellationToken cancellationToken)
    {
        // Notifications don't have an ID
        var json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = notification.Method,
            @params = notification.Params
        }, JsonOptions);

        await connection.Input.WriteLineAsync(json);
        await connection.Input.FlushAsync();
    }

    #endregion

    public void Dispose()
    {
        foreach (var connection in _connections.Values)
        {
            try
            {
                connection.Process?.Kill();
                connection.Process?.Dispose();
            }
            catch { /* Ignore */ }
        }
        _connections.Clear();
    }

    private class MCPServerConnection
    {
        public int ConfigId { get; set; }
        public Process? Process { get; set; }
        public StreamWriter Input { get; set; } = null!;
        public StreamReader Output { get; set; } = null!;
    }
}
