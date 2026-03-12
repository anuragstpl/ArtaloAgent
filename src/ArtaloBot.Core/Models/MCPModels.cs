using System.Text.Json.Serialization;

namespace ArtaloBot.Core.Models;

/// <summary>
/// Configuration for an MCP (Model Context Protocol) server.
/// </summary>
public class MCPServerConfig
{
    public int Id { get; set; }

    /// <summary>Display name for the server.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description of what this server provides.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Server type: "stdio", "http", "websocket".</summary>
    public string ServerType { get; set; } = "stdio";

    /// <summary>For stdio: the command to run (e.g., "npx", "python").</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>For stdio: arguments for the command (JSON array).</summary>
    public string Arguments { get; set; } = "[]";

    /// <summary>For stdio: working directory.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>For stdio: environment variables (JSON object).</summary>
    public string EnvironmentVariables { get; set; } = "{}";

    /// <summary>For http/websocket: the URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Whether this server is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Auto-start when application launches.</summary>
    public bool AutoStart { get; set; } = false;

    /// <summary>Cached list of tools (JSON).</summary>
    public string CachedTools { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a tool exposed by an MCP server.
/// </summary>
public class MCPTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public MCPToolInputSchema? InputSchema { get; set; }
}

public class MCPToolInputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, MCPToolProperty>? Properties { get; set; }

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }
}

public class MCPToolProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("enum")]
    public List<string>? Enum { get; set; }
}

/// <summary>
/// Result of calling an MCP tool.
/// </summary>
public class MCPToolResult
{
    public bool Success { get; set; }
    public string? Content { get; set; }
    public string? Error { get; set; }
    public bool IsError { get; set; }
}

/// <summary>
/// Status of an MCP server connection.
/// </summary>
public enum MCPServerStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

/// <summary>
/// Runtime state of an MCP server.
/// </summary>
public class MCPServerState
{
    public int ConfigId { get; set; }
    public MCPServerStatus Status { get; set; } = MCPServerStatus.Disconnected;
    public string? ErrorMessage { get; set; }
    public List<MCPTool> Tools { get; set; } = [];
    public DateTime? LastConnected { get; set; }
}

#region JSON-RPC Messages

public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public object? Params { get; set; }
}

public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }
}

public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public class MCPInitializeParams
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    [JsonPropertyName("capabilities")]
    public MCPClientCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("clientInfo")]
    public MCPClientInfo ClientInfo { get; set; } = new();
}

public class MCPClientCapabilities
{
    [JsonPropertyName("tools")]
    public object? Tools { get; set; } = new { };
}

public class MCPClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "ArtaloBot";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";
}

public class MCPToolCallParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, object>? Arguments { get; set; }
}

public class MCPToolsListResult
{
    [JsonPropertyName("tools")]
    public List<MCPTool>? Tools { get; set; }
}

public class MCPToolCallResult
{
    [JsonPropertyName("content")]
    public List<MCPContent>? Content { get; set; }

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}

public class MCPContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

#endregion
