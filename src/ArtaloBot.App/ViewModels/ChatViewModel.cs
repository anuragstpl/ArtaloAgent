using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using ArtaloBot.Services.LLM;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArtaloBot.App.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IChatRepository _chatRepository;
    private readonly ISettingsService _settingsService;
    private readonly ILLMServiceFactory _llmFactory;
    private readonly IMemoryService _memoryService;
    private readonly IDebugService _debugService;
    private readonly IMCPService _mcpService;
    private CancellationTokenSource? _streamingCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessages))]
    private ObservableCollection<ChatMessageViewModel> _messages = [];

    public bool HasMessages => Messages.Count > 0;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private LLMProviderType _selectedProvider = LLMProviderType.Ollama;

    [ObservableProperty]
    private string _selectedModel = "llama3.2";

    [ObservableProperty]
    private ObservableCollection<string> _availableModels = ["llama3.2", "llama3.1", "mistral"];

    [ObservableProperty]
    private ChatSession? _currentSession;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isMemoryEnabled;

    [ObservableProperty]
    private int _memoryCount;

    public ObservableCollection<LLMProviderType> AvailableProviders { get; } =
    [
        LLMProviderType.OpenAI,
        LLMProviderType.Gemini,
        LLMProviderType.Ollama
    ];

    public ChatViewModel(
        IChatRepository chatRepository,
        ISettingsService settingsService,
        ILLMServiceFactory llmFactory,
        IMemoryService memoryService,
        IDebugService debugService,
        IMCPService mcpService)
    {
        _chatRepository = chatRepository;
        _settingsService = settingsService;
        _llmFactory = llmFactory;
        _memoryService = memoryService;
        _debugService = debugService;
        _mcpService = mcpService;

        // Subscribe to collection changes to update HasMessages
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));

        _debugService.Info("Chat", "ChatViewModel initialized");

        // Initialize Ollama service by default (no API key needed)
        InitializeOllamaAsync();
        _ = RefreshMemoryStatusAsync();
    }

    private async void InitializeOllamaAsync()
    {
        try
        {
            var service = _llmFactory.GetService(LLMProviderType.Ollama);
            if (service == null)
            {
                _llmFactory.CreateService(LLMProviderType.Ollama, "", "http://localhost:11434");
                service = _llmFactory.GetService(LLMProviderType.Ollama);
            }

            if (service != null)
            {
                var models = await service.GetAvailableModelsAsync();
                var modelList = models.ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableModels.Clear();
                    foreach (var model in modelList)
                    {
                        AvailableModels.Add(model);
                    }
                    if (AvailableModels.Count > 0)
                    {
                        SelectedModel = AvailableModels[0];
                    }
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ollama not available: {ex.Message}";
        }
    }

    private async Task RefreshMemoryStatusAsync()
    {
        try
        {
            var settings = await _settingsService.GetMemorySettingsAsync();
            IsMemoryEnabled = settings.IsEnabled;

            var sessionId = CurrentSession?.Id;
            MemoryCount = await _memoryService.GetMemoryCountAsync(sessionId);
        }
        catch { /* non-critical */ }
    }

    partial void OnSelectedProviderChanged(LLMProviderType value)
    {
        _ = LoadAvailableModelsAsync();
    }

    public async Task LoadSessionAsync(int sessionId)
    {
        var session = await _chatRepository.GetSessionAsync(sessionId);
        if (session == null) return;

        CurrentSession = session;
        SelectedProvider = session.Provider;
        SelectedModel = session.Model;

        Messages.Clear();
        foreach (var msg in session.Messages)
        {
            Messages.Add(new ChatMessageViewModel(msg));
        }

        await RefreshMemoryStatusAsync();
    }

    public async Task LoadAvailableModelsAsync()
    {
        try
        {
            var service = _llmFactory.GetService(SelectedProvider);
            if (service == null)
            {
                // For Ollama, create service without API key
                if (SelectedProvider == LLMProviderType.Ollama)
                {
                    _llmFactory.CreateService(SelectedProvider, "", "http://localhost:11434");
                    service = _llmFactory.GetService(SelectedProvider);
                }
                else
                {
                    var provider = await _settingsService.GetProviderSettingsAsync(SelectedProvider);
                    if (provider != null && !string.IsNullOrEmpty(provider.ApiKey))
                    {
                        _llmFactory.CreateService(SelectedProvider, provider.ApiKey, provider.BaseUrl);
                        service = _llmFactory.GetService(SelectedProvider);
                    }
                }
            }

            if (service != null)
            {
                var models = await service.GetAvailableModelsAsync();
                AvailableModels.Clear();
                foreach (var model in models)
                {
                    AvailableModels.Add(model);
                }

                if (AvailableModels.Count > 0 && !AvailableModels.Contains(SelectedModel))
                {
                    SelectedModel = AvailableModels[0];
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading models: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var userInput = InputText.Trim();
        InputText = string.Empty;

        _debugService.Info("Chat", "Processing user message",
            $"Length: {userInput.Length} chars, Provider: {SelectedProvider}, Model: {SelectedModel}");

        // Ensure we have a session
        if (CurrentSession == null)
        {
            _debugService.Info("Chat", "Creating new chat session");
            CurrentSession = await _chatRepository.CreateSessionAsync(new ChatSession
            {
                Title = userInput.Length > 50 ? userInput[..50] + "..." : userInput,
                Provider = SelectedProvider,
                Model = SelectedModel
            });
            _debugService.Success("Chat", $"Created session #{CurrentSession.Id}");
        }

        // Add user message
        var userMessage = new ChatMessage
        {
            SessionId = CurrentSession.Id,
            Role = MessageRole.User,
            Content = userInput,
            Provider = SelectedProvider,
            Model = SelectedModel
        };

        await _chatRepository.AddMessageAsync(userMessage);
        Messages.Add(new ChatMessageViewModel(userMessage));
        _debugService.Info("Chat", "User message added to chat history");

        // Check if this is a tool-related query (real-time data)
        var isToolRelated = IsToolRelatedQuery(userInput);

        // Only store user message in memory if it's NOT a tool-related query
        if (!isToolRelated)
        {
            _ = StoreMessageInMemoryAsync(userInput, MessageRole.User);
        }
        else
        {
            _debugService.Info("Memory", "Skipping memory storage - tool-related query");
        }

        // Get AI response
        await GetAIResponseAsync();
    }

    private async Task StoreMessageInMemoryAsync(string content, MessageRole role)
    {
        try
        {
            var memSettings = await _settingsService.GetMemorySettingsAsync();
            if (!memSettings.IsEnabled) return;

            await _memoryService.StoreMemoryAsync(
                content, role,
                sessionId: CurrentSession?.Id,
                cancellationToken: CancellationToken.None);

            // Refresh memory count display
            var count = await _memoryService.GetMemoryCountAsync(CurrentSession?.Id);
            Application.Current.Dispatcher.Invoke(() => MemoryCount = count);
        }
        catch { /* non-critical */ }
    }

    private string BuildMCPToolsPrompt()
    {
        var tools = _mcpService.GetAllAvailableTools();
        if (tools.Count == 0) return string.Empty;

        _debugService.Info("MCP", $"Building tools prompt with {tools.Count} available tools");

        var sb = new StringBuilder();
        sb.AppendLine("\n## TOOLS");
        sb.AppendLine("You have tools for real-time data. When user asks about current time, weather, etc., you MUST call a tool.");
        sb.AppendLine();
        sb.AppendLine("To call a tool, output ONLY this JSON block (nothing else before it):");
        sb.AppendLine("```tool_call");
        sb.AppendLine("{\"tool\": \"TOOL_NAME\", \"arguments\": {\"param\": \"value\"}}");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Available tools:");

        foreach (var (server, tool) in tools)
        {
            sb.AppendLine($"\n{tool.Name}: {tool.Description ?? "No description"}");

            if (tool.InputSchema?.Properties != null && tool.InputSchema.Properties.Count > 0)
            {
                var required = tool.InputSchema.Required ?? [];
                sb.AppendLine("  Parameters:");
                foreach (var prop in tool.InputSchema.Properties)
                {
                    var req = required.Contains(prop.Key) ? " (required)" : "";
                    sb.AppendLine($"    - {prop.Key}: {prop.Value.Type}{req}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("Example: User asks 'what time is it in Tokyo?'");
        sb.AppendLine("You respond with:");
        sb.AppendLine("```tool_call");
        sb.AppendLine("{\"tool\": \"get_current_time\", \"arguments\": {\"timezone\": \"Asia/Tokyo\"}}");
        sb.AppendLine("```");

        _debugService.Info("MCP", $"Tools prompt size: {sb.Length} chars");
        return sb.ToString();
    }

    private bool IsToolRelatedQuery(string query)
    {
        // Check if the query is asking for real-time information that should use tools
        var toolKeywords = new[]
        {
            // Time queries
            "current time", "what time", "time in", "time now", "what's the time",
            "tell me the time", "give me the time", "show time",
            // Weather queries
            "current weather", "weather in", "temperature in", "how's the weather",
            "what's the weather", "is it raining", "is it sunny",
            // General real-time
            "right now", "currently", "at the moment", "at present",
            "today's date", "what date", "what day",
            // Search queries
            "search for", "look up", "find me", "google",
            // File operations
            "read file", "write file", "list files", "create file"
        };

        var lowerQuery = query.ToLowerInvariant();
        var hasToolKeyword = toolKeywords.Any(keyword => lowerQuery.Contains(keyword));

        if (hasToolKeyword)
        {
            _debugService.Info("Chat", "Tool-related query detected", $"Query: {query}");
        }

        return hasToolKeyword;
    }

    private bool ShouldStoreInMemory(string content, bool isToolRelated)
    {
        // Don't store tool-related responses in memory to avoid confusion
        if (isToolRelated) return false;

        // Don't store very short responses
        if (content.Length < 20) return false;

        // Don't store error messages
        if (content.StartsWith("Error:")) return false;

        return true;
    }

    private static string GetTimeAgo(DateTime dateTime)
    {
        var span = DateTime.UtcNow - dateTime;

        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return dateTime.ToString("MMM d");
    }

    private (string? toolName, Dictionary<string, object>? arguments) ParseToolCall(string response)
    {
        // Try multiple patterns - models output tool calls differently
        var patterns = new[]
        {
            @"```tool_call\s*\n?\s*(\{[\s\S]*?\})\s*\n?```",  // Standard format
            @"```json\s*\n?\s*(\{[\s\S]*?""tool""[\s\S]*?\})\s*\n?```",  // json block with tool
            @"```\s*\n?\s*(\{[\s\S]*?""tool""[\s\S]*?\})\s*\n?```",  // plain code block
            @"(\{[\s\S]*?""tool""\s*:\s*""[^""]+""[\s\S]*?\})"  // Raw JSON with tool key
        };

        string? jsonMatch = null;
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(response, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                jsonMatch = match.Groups[1].Value.Trim();
                _debugService.Info("MCP", $"Found tool call with pattern", pattern);
                break;
            }
        }

        if (jsonMatch == null)
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonMatch);
            var root = doc.RootElement;

            string? toolName = null;

            // Try different property names for the tool
            if (root.TryGetProperty("tool", out var toolProp))
                toolName = toolProp.GetString();
            else if (root.TryGetProperty("name", out var nameProp))
                toolName = nameProp.GetString();
            else if (root.TryGetProperty("function", out var funcProp))
                toolName = funcProp.GetString();

            if (string.IsNullOrEmpty(toolName))
            {
                _debugService.Warning("MCP", "Tool call JSON found but no tool name");
                return (null, null);
            }

            Dictionary<string, object>? arguments = null;

            // Try different property names for arguments
            if (root.TryGetProperty("arguments", out var argsElement))
                arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsElement.GetRawText());
            else if (root.TryGetProperty("args", out var args2Element))
                arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(args2Element.GetRawText());
            else if (root.TryGetProperty("parameters", out var paramsElement))
                arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(paramsElement.GetRawText());

            _debugService.Info("MCP", $"Parsed tool call: {toolName}",
                $"Arguments: {JsonSerializer.Serialize(arguments)}");

            return (toolName, arguments);
        }
        catch (Exception ex)
        {
            _debugService.Warning("MCP", $"Failed to parse tool call: {ex.Message}", jsonMatch);
            return (null, null);
        }
    }

    private async Task<string?> ExecuteMCPToolAsync(string toolName, Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        _debugService.Info("MCP", $"Executing tool: {toolName}");

        var result = await _mcpService.CallToolByNameAsync(toolName, arguments, cancellationToken);

        if (result.Success)
        {
            _debugService.Success("MCP", $"Tool {toolName} executed successfully",
                result.Content?.Length > 200 ? result.Content[..200] + "..." : result.Content);
            return result.Content;
        }
        else
        {
            _debugService.Error("MCP", $"Tool {toolName} failed", result.Error);
            return $"Error: {result.Error}";
        }
    }

    private async Task GetAIResponseAsync()
    {
        if (CurrentSession == null) return;

        _debugService.Info("LLM", $"Initializing {SelectedProvider} service");

        var service = _llmFactory.GetService(SelectedProvider);
        if (service == null)
        {
            // For Ollama, create service without API key
            if (SelectedProvider == LLMProviderType.Ollama)
            {
                _debugService.Info("LLM", "Creating Ollama service (no API key required)");
                _llmFactory.CreateService(SelectedProvider, "", "http://localhost:11434");
                service = _llmFactory.GetService(SelectedProvider);
            }
            else
            {
                var provider = await _settingsService.GetProviderSettingsAsync(SelectedProvider);
                if (provider == null || string.IsNullOrEmpty(provider.ApiKey))
                {
                    _debugService.Error("LLM", $"No API key configured for {SelectedProvider}");
                    StatusMessage = $"Please configure {SelectedProvider} API key in Settings";
                    return;
                }

                _debugService.Info("LLM", $"Creating {SelectedProvider} service with API key");
                _llmFactory.CreateService(SelectedProvider, provider.ApiKey, provider.BaseUrl);
                service = _llmFactory.GetService(SelectedProvider);
            }
        }

        if (service == null)
        {
            _debugService.Error("LLM", "Failed to initialize LLM service");
            StatusMessage = "Failed to initialize LLM service";
            return;
        }

        _debugService.Success("LLM", $"{SelectedProvider} service ready");

        // Create assistant message placeholder
        var assistantMessage = new ChatMessage
        {
            SessionId = CurrentSession.Id,
            Role = MessageRole.Assistant,
            Content = "",
            Provider = SelectedProvider,
            Model = SelectedModel,
            IsStreaming = true
        };

        var messageVm = new ChatMessageViewModel(assistantMessage);
        Messages.Add(messageVm);

        IsStreaming = true;
        StatusMessage = "Generating response...";
        _streamingCts = new CancellationTokenSource();

        try
        {
            var settings = await _settingsService.GetDefaultLLMSettingsAsync();
            settings.Model = SelectedModel;

            _debugService.Info("LLM", $"Using model: {SelectedModel}",
                $"Temperature: {settings.Temperature}, MaxTokens: {settings.MaxTokens}");

            // ── Determine query type ────────────────────────────────────────
            var lastUserMsg = Messages
                .Where(m => m.Role == MessageRole.User && !m.IsStreaming)
                .LastOrDefault()?.Content ?? string.Empty;

            var isToolRelatedQuery = !string.IsNullOrEmpty(lastUserMsg) && IsToolRelatedQuery(lastUserMsg);
            var hasAvailableTools = _mcpService.GetAllAvailableTools().Count > 0;

            _debugService.Info("Chat", $"Query analysis",
                $"IsToolRelated: {isToolRelatedQuery}, HasTools: {hasAvailableTools}");

            // ── Memory injection ────────────────────────────────────────────
            var memSettings = await _settingsService.GetMemorySettingsAsync();
            string memoryContext = string.Empty;

            // Only search memory for NON-tool-related queries
            // Tool queries should always use fresh tool calls, not cached memory
            if (memSettings.IsEnabled && _memoryService.IsReady && !isToolRelatedQuery)
            {
                _debugService.Info("Memory", "Memory enabled, searching for relevant context",
                    $"Provider: {memSettings.EmbeddingProvider}, Model: {memSettings.EmbeddingModel}\nCrossSession: {memSettings.CrossSessionMemory}, StoreGlobally: {memSettings.StoreGlobally}");

                if (!string.IsNullOrEmpty(lastUserMsg))
                {
                    StatusMessage = "Searching memories...";

                    // Pass null for sessionId to search ALL memories globally
                    // The service will decide based on CrossSessionMemory setting
                    var memories = await _memoryService.SearchMemoriesAsync(
                        lastUserMsg,
                        sessionId: null,  // Search globally
                        topK: memSettings.MaxMemoriesToInject,
                        similarityThreshold: memSettings.SimilarityThreshold,
                        cancellationToken: _streamingCts.Token);

                    if (memories.Count > 0)
                    {
                        _debugService.Success("Memory", $"Injecting {memories.Count} memories into context");

                        var sb = new StringBuilder();
                        sb.AppendLine("## CONTEXT (Use this to answer)");
                        sb.AppendLine("Answer ONLY based on this context. If the answer is not in the context, say you don't know.");
                        sb.AppendLine();
                        foreach (var mem in memories)
                        {
                            var roleName = mem.Entry.Role == MessageRole.User ? "Q" : "A";
                            sb.AppendLine($"{roleName}: {mem.Entry.Content}");
                        }
                        memoryContext = sb.ToString();
                    }
                    else
                    {
                        _debugService.Info("Memory", "No relevant memories found for this query");
                    }
                }
            }
            else if (isToolRelatedQuery)
            {
                _debugService.Info("Memory", "Skipping memory search - this is a tool-related query (real-time data needed)");
            }
            else
            {
                _debugService.Info("Memory", "Memory disabled or not ready, skipping");
            }

            // Build effective system prompt
            var effectiveSystemPrompt = settings.SystemPrompt;

            // ── MCP Tools injection ────────────────────────────────────────
            var mcpToolsPrompt = BuildMCPToolsPrompt();

            // Warn if tool-related query but no tools available
            if (isToolRelatedQuery && string.IsNullOrEmpty(mcpToolsPrompt))
            {
                _debugService.Warning("MCP", "Tool-related query detected but NO MCP tools are connected!",
                    "Go to Skills page and connect your MCP servers");
            }

            // For tool-related queries, put tools FIRST so the model sees them
            if (isToolRelatedQuery && !string.IsNullOrEmpty(mcpToolsPrompt))
            {
                effectiveSystemPrompt = mcpToolsPrompt + "\n\n" + (effectiveSystemPrompt ?? "");
                _debugService.Info("LLM", "Tool-related query: Tools placed at START of system prompt");
            }
            else if (!string.IsNullOrEmpty(mcpToolsPrompt))
            {
                effectiveSystemPrompt = string.IsNullOrEmpty(effectiveSystemPrompt)
                    ? mcpToolsPrompt
                    : effectiveSystemPrompt + "\n\n" + mcpToolsPrompt;
                _debugService.Info("LLM", "System prompt augmented with MCP tools");
            }
            // ───────────────────────────────────────────────────────────────

            // Inject memory context (only for non-tool queries, already filtered above)
            if (!string.IsNullOrEmpty(memoryContext))
            {
                effectiveSystemPrompt = string.IsNullOrEmpty(effectiveSystemPrompt)
                    ? memoryContext
                    : effectiveSystemPrompt + "\n\n" + memoryContext;
                _debugService.Info("LLM", "System prompt augmented with memory context");
            }

            settings.SystemPrompt = effectiveSystemPrompt;
            // ───────────────────────────────────────────────────────────────

            var chatMessages = Messages
                .Where(m => !m.IsStreaming || m == messageVm)
                .Select(m => new ChatMessage
                {
                    Role = m.Role,
                    Content = m.Content
                })
                .SkipLast(1) // Skip the empty assistant message
                .ToList();

            _debugService.Info("LLM", $"Sending request with {chatMessages.Count} messages",
                $"System prompt: {effectiveSystemPrompt?.Length ?? 0} chars");

            // Verify Ollama is reachable before sending
            if (SelectedProvider == LLMProviderType.Ollama)
            {
                StatusMessage = "Connecting to Ollama...";
                var isConnected = await service.ValidateConnectionAsync(_streamingCts.Token);
                if (!isConnected)
                {
                    throw new InvalidOperationException("Cannot connect to Ollama. Make sure Ollama is running (ollama serve).");
                }
            }

            var responseBuilder = new StringBuilder();
            var tokenCount = 0;
            var startTime = DateTime.Now;

            StatusMessage = $"Waiting for {SelectedModel}...";

            _debugService.Info("LLM", "Starting streaming response...");

            await foreach (var chunk in service.StreamMessageAsync(chatMessages, settings, _streamingCts.Token))
            {
                responseBuilder.Append(chunk);
                tokenCount++;

                // Update status to show progress
                if (tokenCount == 1)
                {
                    StatusMessage = "Generating response...";
                }

                var currentContent = responseBuilder.ToString();

                // Update on UI thread - use BeginInvoke to avoid blocking the async stream
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    messageVm.Content = currentContent;
                });
            }

            var elapsed = DateTime.Now - startTime;

            // Check for MCP tool calls in the response
            var fullResponse = responseBuilder.ToString();
            var (toolName, toolArgs) = ParseToolCall(fullResponse);

            if (toolName != null)
            {
                _debugService.Info("MCP", $"Tool call detected: {toolName}");
                StatusMessage = $"Executing tool: {toolName}...";

                // Execute the tool
                var toolResult = await ExecuteMCPToolAsync(toolName, toolArgs, _streamingCts?.Token ?? CancellationToken.None);

                // Add tool result to the conversation and get a follow-up response
                Application.Current.Dispatcher.Invoke(() =>
                {
                    messageVm.Content = fullResponse + $"\n\n**Tool Result:**\n```\n{toolResult}\n```";
                });

                // Add the tool response as a system message and get final response
                var toolResultMessage = new ChatMessage
                {
                    Role = MessageRole.User,
                    Content = $"Tool '{toolName}' returned: {toolResult}\n\nNow provide a natural language response to the user based on this result."
                };

                var followUpMessages = Messages
                    .Where(m => !m.IsStreaming || m == messageVm)
                    .Select(m => new ChatMessage
                    {
                        Role = m.Role,
                        Content = m.Content
                    })
                    .ToList();
                followUpMessages.Add(toolResultMessage);

                // Get follow-up response
                _debugService.Info("LLM", "Getting follow-up response after tool execution");
                StatusMessage = "Generating response...";

                var followUpBuilder = new StringBuilder();
                await foreach (var chunk in service.StreamMessageAsync(followUpMessages, settings, _streamingCts?.Token ?? CancellationToken.None))
                {
                    followUpBuilder.Append(chunk);
                    var currentContent = followUpBuilder.ToString();
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        messageVm.Content = currentContent;
                    });
                }

                fullResponse = followUpBuilder.ToString();
            }

            // Save the complete message
            assistantMessage.Content = fullResponse;
            assistantMessage.IsStreaming = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                messageVm.IsStreaming = false;
            });

            _debugService.Success("LLM", $"Response complete",
                $"Tokens: ~{tokenCount}, Time: {elapsed.TotalSeconds:F2}s, Length: {assistantMessage.Content.Length} chars");

            await _chatRepository.AddMessageAsync(assistantMessage);
            StatusMessage = IsMemoryEnabled ? $"Ready · {MemoryCount} memories" : "Ready";

            // Store assistant response in memory ONLY if it's not a tool-related response
            if (!isToolRelatedQuery && ShouldStoreInMemory(assistantMessage.Content, isToolRelatedQuery))
            {
                _debugService.Info("Memory", "Storing assistant response in memory");
                _ = StoreMessageInMemoryAsync(assistantMessage.Content, MessageRole.Assistant);
            }
            else
            {
                _debugService.Info("Memory", "Skipping memory storage - tool-related or not suitable");
            }
        }
        catch (OperationCanceledException)
        {
            _debugService.Warning("LLM", "Response generation cancelled by user");
            StatusMessage = "Response cancelled";
        }
        catch (Exception ex)
        {
            _debugService.Error("LLM", $"Error generating response: {ex.Message}",
                $"Exception: {ex.GetType().Name}\n{ex.StackTrace}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                messageVm.Content = $"Error: {ex.Message}";
                messageVm.IsError = true;
            });
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsStreaming = false;
            _streamingCts?.Dispose();
            _streamingCts = null;
        }
    }

    [RelayCommand]
    private void StopGeneration()
    {
        _streamingCts?.Cancel();
    }

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        CurrentSession = null;
        MemoryCount = 0;
        StatusMessage = "Chat cleared";
    }

    [RelayCommand]
    private async Task ClearSessionMemory()
    {
        if (CurrentSession == null) return;
        await _memoryService.ClearMemoriesAsync(CurrentSession.Id);
        await RefreshMemoryStatusAsync();
        StatusMessage = "Session memory cleared";
    }

    [RelayCommand]
    private async Task ClearAllMemories()
    {
        await _memoryService.ClearAllMemoriesAsync();
        await RefreshMemoryStatusAsync();
        StatusMessage = "All memories cleared";
        _debugService.Info("Memory", "All memories cleared by user");
    }
}

public partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUser))]
    [NotifyPropertyChangedFor(nameof(IsAssistant))]
    private MessageRole _role;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private DateTime _timestamp;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private string? _model;

    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;

    public ChatMessageViewModel(ChatMessage message)
    {
        Role = message.Role;
        Content = message.Content;
        Timestamp = message.Timestamp;
        IsStreaming = message.IsStreaming;
        IsError = message.IsError;
        Model = message.Model;
    }
}
