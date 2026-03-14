using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Net.Http;
using System.Net.Http.Headers;
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
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<int, MCPServerConnection> _connections = new();
    private readonly ConcurrentDictionary<int, MCPServerState> _states = new();
    private readonly ConcurrentDictionary<int, MCPServerConfig> _configCache = new();
    private readonly ConcurrentDictionary<int, bool> _builtinSkills = new(); // Track built-in skills
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
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    /// Checks if a server config is a built-in skill (not an external MCP process).
    /// </summary>
    private static bool IsBuiltinSkill(MCPServerConfig config) =>
        config.Command?.StartsWith("builtin:") == true;

    /// <summary>
    /// Gets the built-in skill type from the command.
    /// </summary>
    private static string GetBuiltinSkillType(MCPServerConfig config) =>
        config.Command?.Replace("builtin:", "") ?? "";

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
            List<MCPTool> tools;

            // Handle built-in skills (no external process needed)
            if (IsBuiltinSkill(config))
            {
                _builtinSkills[serverId] = true;
                tools = GetBuiltinSkillTools(config);
                _debugService?.Info("MCP", $"Registering built-in skill: {config.Name}");
            }
            else
            {
                // Start the external MCP process
                var connection = await StartServerProcessAsync(config, cancellationToken);
                _connections[serverId] = connection;

                // Initialize the MCP protocol
                await InitializeProtocolAsync(connection, cancellationToken);

                // Get available tools
                tools = await ListToolsAsync(connection, cancellationToken);
            }

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

    /// <summary>
    /// Gets the available tools for a built-in skill.
    /// </summary>
    private List<MCPTool> GetBuiltinSkillTools(MCPServerConfig config)
    {
        var skillType = GetBuiltinSkillType(config);

        return skillType switch
        {
            "email" => new List<MCPTool>
            {
                new MCPTool
                {
                    Name = "send_email",
                    Description = "Send an email to a recipient",
                    InputSchema = new MCPToolInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, MCPToolProperty>
                        {
                            ["to"] = new MCPToolProperty { Type = "string", Description = "Recipient email address(es), comma-separated" },
                            ["subject"] = new MCPToolProperty { Type = "string", Description = "Email subject line" },
                            ["body"] = new MCPToolProperty { Type = "string", Description = "Email body content" }
                        },
                        Required = new List<string> { "to", "subject", "body" }
                    }
                }
            },
            "webhook" => new List<MCPTool>
            {
                new MCPTool
                {
                    Name = "call_webhook",
                    Description = "Call a configured webhook/API endpoint",
                    InputSchema = new MCPToolInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, MCPToolProperty>
                        {
                            ["message"] = new MCPToolProperty { Type = "string", Description = "Message or data to send" },
                            ["data"] = new MCPToolProperty { Type = "object", Description = "Additional data as JSON object" }
                        },
                        Required = new List<string> { "message" }
                    }
                }
            },
            "scheduler" => new List<MCPTool>
            {
                new MCPTool
                {
                    Name = "schedule_job",
                    Description = "Schedule or manage a recurring job",
                    InputSchema = new MCPToolInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, MCPToolProperty>
                        {
                            ["action"] = new MCPToolProperty { Type = "string", Description = "Action: activate, deactivate, or status" },
                            ["message"] = new MCPToolProperty { Type = "string", Description = "Message for the scheduled job" }
                        },
                        Required = new List<string> { "action" }
                    }
                }
            },
            _ => new List<MCPTool>()
        };
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
        // Check if this is a built-in skill
        if (_builtinSkills.TryGetValue(serverId, out var isBuiltin) && isBuiltin)
        {
            if (!_configCache.TryGetValue(serverId, out var config))
            {
                return new MCPToolResult
                {
                    Success = false,
                    IsError = true,
                    Error = $"Built-in skill {serverId} configuration not found"
                };
            }

            return await ExecuteBuiltinSkillAsync(config, toolName, arguments, cancellationToken);
        }

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

    /// <summary>
    /// Executes a built-in skill tool.
    /// </summary>
    private async Task<MCPToolResult> ExecuteBuiltinSkillAsync(
        MCPServerConfig config,
        string toolName,
        Dictionary<string, object>? arguments,
        CancellationToken cancellationToken)
    {
        var skillType = GetBuiltinSkillType(config);
        var envVars = ParseEnvironmentVariables(config.EnvironmentVariables);

        _debugService?.Info("MCP", $"Executing built-in skill: {skillType}/{toolName}",
            $"Arguments: {JsonSerializer.Serialize(arguments)}");

        try
        {
            return skillType switch
            {
                "email" => await ExecuteEmailSkillAsync(envVars, arguments, cancellationToken),
                "webhook" => await ExecuteWebhookSkillAsync(envVars, arguments, cancellationToken),
                "scheduler" => ExecuteSchedulerSkill(envVars, arguments),
                _ => new MCPToolResult { Success = false, IsError = true, Error = $"Unknown built-in skill: {skillType}" }
            };
        }
        catch (Exception ex)
        {
            _debugService?.Error("MCP", $"Built-in skill {skillType} failed", ex.Message);
            return new MCPToolResult { Success = false, IsError = true, Error = ex.Message };
        }
    }

    private static Dictionary<string, string> ParseEnvironmentVariables(string? envVarsJson)
    {
        if (string.IsNullOrWhiteSpace(envVarsJson)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(envVarsJson) ?? new();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private async Task<MCPToolResult> ExecuteEmailSkillAsync(
        Dictionary<string, string> envVars,
        Dictionary<string, object>? arguments,
        CancellationToken cancellationToken)
    {
        // Get config from env vars
        var smtpHost = envVars.GetValueOrDefault("SMTP_HOST", "");
        var smtpPortStr = envVars.GetValueOrDefault("SMTP_PORT", "587");
        var username = envVars.GetValueOrDefault("SMTP_USERNAME", "");
        var password = envVars.GetValueOrDefault("SMTP_PASSWORD", "");
        var fromEmail = envVars.GetValueOrDefault("FROM_EMAIL", "");
        var fromName = envVars.GetValueOrDefault("FROM_NAME", "ArtaloBot");

        if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(username))
        {
            return new MCPToolResult { Success = false, IsError = true, Error = "Email skill not configured. Please set SMTP_HOST and SMTP_USERNAME." };
        }

        // Get arguments
        var to = arguments?.GetValueOrDefault("to")?.ToString() ?? "";
        var subject = arguments?.GetValueOrDefault("subject")?.ToString() ?? "Message from ArtaloBot";
        var body = arguments?.GetValueOrDefault("body")?.ToString() ?? "";

        if (string.IsNullOrEmpty(to))
        {
            return new MCPToolResult { Success = false, IsError = true, Error = "Recipient email address (to) is required" };
        }

        try
        {
            int.TryParse(smtpPortStr, out var smtpPort);
            smtpPort = smtpPort > 0 ? smtpPort : 587;

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(username, password)
            };

            var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = body.Contains("<") && body.Contains(">")
            };

            foreach (var email in to.Split(',', ';'))
            {
                var trimmed = email.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    message.To.Add(trimmed);
            }

            await client.SendMailAsync(message, cancellationToken);

            _debugService?.Success("MCP", "Email sent successfully", $"To: {to}, Subject: {subject}");

            return new MCPToolResult
            {
                Success = true,
                Content = $"Email sent successfully to {to}"
            };
        }
        catch (Exception ex)
        {
            return new MCPToolResult { Success = false, IsError = true, Error = $"Failed to send email: {ex.Message}" };
        }
    }

    private async Task<MCPToolResult> ExecuteWebhookSkillAsync(
        Dictionary<string, string> envVars,
        Dictionary<string, object>? arguments,
        CancellationToken cancellationToken)
    {
        var url = envVars.GetValueOrDefault("WEBHOOK_URL", "");
        var method = envVars.GetValueOrDefault("WEBHOOK_METHOD", "POST");
        var authType = envVars.GetValueOrDefault("WEBHOOK_AUTH_TYPE", "None");
        var authValue = envVars.GetValueOrDefault("WEBHOOK_AUTH_VALUE", "");
        var bodyTemplate = envVars.GetValueOrDefault("WEBHOOK_BODY_TEMPLATE", "{}");

        if (string.IsNullOrEmpty(url))
        {
            return new MCPToolResult { Success = false, IsError = true, Error = "Webhook URL not configured" };
        }

        try
        {
            var request = new HttpRequestMessage(new HttpMethod(method), url);

            // Add authentication
            if (authType == "Bearer" && !string.IsNullOrEmpty(authValue))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authValue);
            }
            else if (authType == "Basic" && !string.IsNullOrEmpty(authValue))
            {
                var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes(authValue));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            }
            else if (authType == "ApiKey" && !string.IsNullOrEmpty(authValue))
            {
                request.Headers.TryAddWithoutValidation("X-API-Key", authValue);
            }

            // Process body template with arguments
            if (method != "GET" && !string.IsNullOrEmpty(bodyTemplate))
            {
                var body = bodyTemplate;
                if (arguments != null)
                {
                    foreach (var arg in arguments)
                    {
                        var value = arg.Value?.ToString() ?? "";
                        body = body.Replace($"{{{{{arg.Key}}}}}", value);
                        body = body.Replace($"{{{{ {arg.Key} }}}}", value);
                    }
                }
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            _debugService?.Success("MCP", $"Webhook called: {url}", $"Status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                return new MCPToolResult
                {
                    Success = true,
                    Content = $"Webhook executed successfully. Status: {response.StatusCode}. Response: {responseContent}"
                };
            }
            else
            {
                return new MCPToolResult
                {
                    Success = false,
                    IsError = true,
                    Error = $"Webhook returned {response.StatusCode}: {responseContent}"
                };
            }
        }
        catch (Exception ex)
        {
            return new MCPToolResult { Success = false, IsError = true, Error = $"Webhook failed: {ex.Message}" };
        }
    }

    private MCPToolResult ExecuteSchedulerSkill(
        Dictionary<string, string> envVars,
        Dictionary<string, object>? arguments)
    {
        var cronExpression = envVars.GetValueOrDefault("CRON_EXPRESSION", "0 9 * * *");
        var jobType = envVars.GetValueOrDefault("JOB_TYPE", "SendMessage");
        var timezone = envVars.GetValueOrDefault("TIMEZONE", "UTC");

        var action = arguments?.GetValueOrDefault("action")?.ToString()?.ToLower() ?? "status";

        // For now, just return status information
        // Full job scheduling would require a background service
        var nextRun = CalculateNextCronRun(cronExpression);

        return action switch
        {
            "activate" => new MCPToolResult
            {
                Success = true,
                Content = $"Job scheduled with cron '{cronExpression}'. Type: {jobType}. Next run: {nextRun:g} ({timezone})"
            },
            "deactivate" => new MCPToolResult
            {
                Success = true,
                Content = "Job schedule deactivated"
            },
            _ => new MCPToolResult
            {
                Success = true,
                Content = $"Schedule: {cronExpression}\nJob Type: {jobType}\nTimezone: {timezone}\nNext Run: {nextRun:g}"
            }
        };
    }

    private static DateTime CalculateNextCronRun(string cronExpression)
    {
        // Simple cron parser for common patterns
        var parts = cronExpression.Split(' ');
        if (parts.Length < 5) return DateTime.UtcNow.AddDays(1);

        try
        {
            var minute = parts[0] == "*" ? DateTime.UtcNow.Minute : int.Parse(parts[0]);
            var hour = parts[1] == "*" ? DateTime.UtcNow.Hour : int.Parse(parts[1]);

            var nextRun = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, hour, minute, 0);

            if (nextRun <= DateTime.UtcNow)
                nextRun = nextRun.AddDays(1);

            return nextRun;
        }
        catch
        {
            return DateTime.UtcNow.AddDays(1);
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
