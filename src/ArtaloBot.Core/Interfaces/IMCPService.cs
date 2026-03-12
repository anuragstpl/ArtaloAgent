using ArtaloBot.Core.Models;

namespace ArtaloBot.Core.Interfaces;

/// <summary>
/// Service for managing MCP server connections and tool calls.
/// </summary>
public interface IMCPService
{
    /// <summary>Get all configured MCP servers.</summary>
    Task<IReadOnlyList<MCPServerConfig>> GetServersAsync(CancellationToken cancellationToken = default);

    /// <summary>Get a specific server configuration.</summary>
    Task<MCPServerConfig?> GetServerAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Add a new MCP server configuration.</summary>
    Task<MCPServerConfig> AddServerAsync(MCPServerConfig config, CancellationToken cancellationToken = default);

    /// <summary>Update an MCP server configuration.</summary>
    Task UpdateServerAsync(MCPServerConfig config, CancellationToken cancellationToken = default);

    /// <summary>Delete an MCP server configuration.</summary>
    Task DeleteServerAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Connect to an MCP server and retrieve its tools.</summary>
    Task<MCPServerState> ConnectAsync(int serverId, CancellationToken cancellationToken = default);

    /// <summary>Disconnect from an MCP server.</summary>
    Task DisconnectAsync(int serverId, CancellationToken cancellationToken = default);

    /// <summary>Get current state of a server.</summary>
    MCPServerState? GetServerState(int serverId);

    /// <summary>Get all connected server states.</summary>
    IReadOnlyList<MCPServerState> GetAllServerStates();

    /// <summary>Get all available tools from all connected servers.</summary>
    IReadOnlyList<(MCPServerConfig Server, MCPTool Tool)> GetAllAvailableTools();

    /// <summary>Call a tool on an MCP server.</summary>
    Task<MCPToolResult> CallToolAsync(
        int serverId,
        string toolName,
        Dictionary<string, object>? arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>Call a tool by name (finds the right server automatically).</summary>
    Task<MCPToolResult> CallToolByNameAsync(
        string toolName,
        Dictionary<string, object>? arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>Event raised when server state changes.</summary>
    event EventHandler<MCPServerState>? ServerStateChanged;
}
