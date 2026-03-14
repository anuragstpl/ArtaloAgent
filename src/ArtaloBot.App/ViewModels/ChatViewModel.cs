using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using ArtaloBot.Services.LLM;
using ArtaloBot.Services.Routing;
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
    private readonly IAgentService _agentService;
    private readonly QueryRouter _queryRouter;
    private CancellationTokenSource? _streamingCts;
    private CancellationTokenSource? _debounceCts;
    private const int DebounceDelayMs = 800; // Wait 800ms for additional messages

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

    // Agent selection for knowledge-based chat
    [ObservableProperty]
    private ObservableCollection<Agent> _availableAgents = [];

    [ObservableProperty]
    private Agent? _selectedAgent;

    [ObservableProperty]
    private bool _useAgentKnowledge;

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
        IMCPService mcpService,
        IAgentService agentService)
    {
        _chatRepository = chatRepository;
        _settingsService = settingsService;
        _llmFactory = llmFactory;
        _memoryService = memoryService;
        _debugService = debugService;
        _mcpService = mcpService;
        _agentService = agentService;

        // Initialize query router for intent-based routing
        _queryRouter = new QueryRouter(mcpService, debugService);

        // Subscribe to collection changes to update HasMessages
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));

        _debugService.Info("Chat", "ChatViewModel initialized with QueryRouter and AgentService");

        // Initialize Ollama service by default (no API key needed)
        InitializeOllamaAsync();
        _ = RefreshMemoryStatusAsync();
        _ = LoadAvailableAgentsAsync();
    }

    /// <summary>
    /// Loads all enabled agents for selection in chat.
    /// </summary>
    public async Task LoadAvailableAgentsAsync()
    {
        try
        {
            var agents = await _agentService.GetAllAgentsAsync();
            var enabledAgents = agents.Where(a => a.IsEnabled).ToList();

            Application.Current.Dispatcher.Invoke(() =>
            {
                AvailableAgents.Clear();
                // Add "None" option represented as null
                foreach (var agent in enabledAgents)
                {
                    AvailableAgents.Add(agent);
                }

                _debugService.Info("Chat", $"Loaded {enabledAgents.Count} agent(s) for chat");
            });
        }
        catch (Exception ex)
        {
            _debugService.Error("Chat", "Failed to load agents", ex.Message);
        }
    }

    partial void OnSelectedAgentChanged(Agent? value)
    {
        UseAgentKnowledge = value != null;
        if (value != null)
        {
            _debugService.Info("Chat", $"Selected agent: {value.Name}",
                $"Documents: {value.Documents?.Count ?? 0}");

            // Check agent's knowledge status
            _ = CheckAgentKnowledgeStatusAsync(value);
        }
        else
        {
            _debugService.Info("Chat", "Agent knowledge disabled");
        }
    }

    private async Task CheckAgentKnowledgeStatusAsync(Agent agent)
    {
        try
        {
            var totalChunks = await _agentService.GetTotalChunksAsync(agent.Id);
            var totalDocs = await _agentService.GetTotalDocumentsAsync(agent.Id);

            if (totalChunks == 0)
            {
                _debugService.Warning("Chat",
                    $"Agent '{agent.Name}' has NO knowledge chunks!",
                    $"Documents: {totalDocs} | Chunks: {totalChunks} - Please upload and process documents");
                StatusMessage = $"Warning: Agent '{agent.Name}' has no processed knowledge";
            }
            else
            {
                _debugService.Success("Chat",
                    $"Agent '{agent.Name}' ready with {totalChunks} knowledge chunks",
                    $"Documents: {totalDocs}");
                StatusMessage = $"Using {agent.Name} ({totalChunks} knowledge chunks)";
            }
        }
        catch (Exception ex)
        {
            _debugService.Error("Chat", "Failed to check agent status", ex.Message);
        }
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

    /// <summary>
    /// Send message with debounce - allows user to send multiple messages before AI responds.
    /// Press Enter to queue messages, they'll be sent after a short delay.
    /// </summary>
    [RelayCommand]
    private async Task SendMessage()
    {
        await SendMessageInternal(useDebounce: true);
    }

    /// <summary>
    /// Send message immediately without debounce (Shift+Enter or button click).
    /// </summary>
    [RelayCommand]
    private async Task SendMessageImmediate()
    {
        await SendMessageInternal(useDebounce: false);
    }

    private async Task SendMessageInternal(bool useDebounce)
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

        // Check if this contains personal information that should ALWAYS be stored
        var containsPersonalInfo = ContainsPersonalInformation(userInput);

        // Store user message in memory if it's NOT a tool-related query OR contains personal info
        if (!isToolRelated || containsPersonalInfo)
        {
            if (containsPersonalInfo)
            {
                _debugService.Info("Memory", "Personal information detected - storing in memory", userInput);
            }
            _ = StoreMessageInMemoryAsync(userInput, MessageRole.User);
        }
        else
        {
            _debugService.Info("Memory", "Skipping memory storage - tool-related query");
        }

        // Cancel any pending debounce
        _debounceCts?.Cancel();

        if (useDebounce)
        {
            // Start debounce - wait for more messages
            _debounceCts = new CancellationTokenSource();
            StatusMessage = "Waiting for more input... (or press Send)";

            try
            {
                await Task.Delay(DebounceDelayMs, _debounceCts.Token);
                // Debounce completed, send to AI
                await GetAIResponseAsync();
            }
            catch (OperationCanceledException)
            {
                // Another message came in, debounce was reset - don't trigger AI yet
                _debugService.Info("Chat", "Debounce cancelled - user sent another message");
            }
        }
        else
        {
            // Immediate send
            await GetAIResponseAsync();
        }
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

    // Note: BuildMCPToolsPrompt removed - now using QueryRouter for direct tool execution

    private bool IsToolRelatedQuery(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        // FIRST: Check if this is a memory/personal query - these should ALWAYS use memory
        var memoryKeywords = new[]
        {
            "my name", "who am i", "what's my", "what is my", "do you remember",
            "did i tell you", "i told you", "you know my", "remember when",
            "we discussed", "we talked about", "earlier i said", "previously"
        };

        if (memoryKeywords.Any(keyword => lowerQuery.Contains(keyword)))
        {
            _debugService.Info("Chat", "Memory/personal query detected - will search memories", $"Query: {query}");
            return false; // NOT a tool query - use memory instead
        }

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

        var hasToolKeyword = toolKeywords.Any(keyword => lowerQuery.Contains(keyword));

        if (hasToolKeyword)
        {
            _debugService.Info("Chat", "Tool-related query detected", $"Query: {query}");
        }

        return hasToolKeyword;
    }

    /// <summary>
    /// Detects if a message contains personal information that should always be stored in memory.
    /// Examples: "my name is", "I am", "I live in", "my email is"
    /// </summary>
    private bool ContainsPersonalInformation(string message)
    {
        var lowerMessage = message.ToLowerInvariant();

        var personalInfoPatterns = new[]
        {
            "my name is", "i'm called", "call me", "i am called",
            "i live in", "i'm from", "i am from", "my location",
            "my email", "my phone", "my number", "my address",
            "my age is", "i am ", "i'm ", "my birthday",
            "my favorite", "my favourite", "i like", "i love", "i hate",
            "i work at", "i work as", "my job", "my profession",
            "remember that", "don't forget", "keep in mind",
            "my wife", "my husband", "my child", "my kids", "my family"
        };

        return personalInfoPatterns.Any(pattern => lowerMessage.Contains(pattern));
    }

    /// <summary>
    /// Detects if a message is a short context-dependent response that refers to previous messages.
    /// Examples: "yes", "ok", "do that", "the first one", "sure", "go ahead"
    /// </summary>
    private bool IsContextReferenceMessage(string message)
    {
        var trimmed = message.Trim().ToLowerInvariant();

        // Very short messages are likely context references
        if (trimmed.Length <= 20)
        {
            var contextPhrases = new[]
            {
                "yes", "no", "ok", "okay", "sure", "yep", "nope", "yeah", "nah",
                "do it", "do that", "go ahead", "proceed", "continue", "next",
                "the first", "the second", "the last", "that one", "this one",
                "option 1", "option 2", "option a", "option b",
                "correct", "right", "exactly", "perfect",
                "please", "thanks", "thank you",
                "why", "how", "what", "when", "where",
                "more", "less", "again", "explain"
            };

            return contextPhrases.Any(phrase => trimmed.Contains(phrase)) || trimmed.Length <= 5;
        }

        return false;
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

    // Note: ParseToolCall removed - now using QueryRouter for direct tool execution

    private async Task<ILLMService?> GetOrCreateLLMServiceAsync()
    {
        _debugService.Info("LLM", $"Initializing {SelectedProvider} service");

        var service = _llmFactory.GetService(SelectedProvider);
        if (service == null)
        {
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
                    return null;
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
            return null;
        }

        _debugService.Success("LLM", $"{SelectedProvider} service ready");
        return service;
    }

    private async Task GetAIResponseAsync()
    {
        if (CurrentSession == null) return;

        // Get the LLM service
        var service = await GetOrCreateLLMServiceAsync();
        if (service == null) return;

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
        _streamingCts = new CancellationTokenSource();

        try
        {
            var settings = await _settingsService.GetDefaultLLMSettingsAsync();
            settings.Model = SelectedModel;

            // Get the last user message for routing
            var lastUserMsg = Messages
                .Where(m => m.Role == MessageRole.User && !m.IsStreaming)
                .LastOrDefault()?.Content ?? string.Empty;

            // ═══════════════════════════════════════════════════════════════
            // ROUTE THE QUERY using QueryRouter
            // ═══════════════════════════════════════════════════════════════
            var route = _queryRouter.Route(lastUserMsg);
            _debugService.Info("Router", $"Query routed to: {route.RouteType}", route.Reason);

            string? toolResultContext = null;
            string? memoryContext = null;
            bool isToolQuery = route.RouteType == QueryRouteType.Tool;

            // ── Handle TOOL route ──────────────────────────────────────────
            if (route.RouteType == QueryRouteType.Tool && !string.IsNullOrEmpty(route.ToolName))
            {
                StatusMessage = $"Calling {route.ToolName}...";
                _debugService.Info("Tool", $"Executing tool directly: {route.ToolName}",
                    $"Arguments: {JsonSerializer.Serialize(route.ToolArguments)}");

                var toolResult = await _mcpService.CallToolByNameAsync(
                    route.ToolName,
                    route.ToolArguments,
                    _streamingCts.Token);

                if (toolResult.Success)
                {
                    _debugService.Success("Tool", $"Tool {route.ToolName} completed", toolResult.Content);
                    toolResultContext = $"You called the tool '{route.ToolName}' and it returned:\n{toolResult.Content}\n\nNow provide a helpful, natural response to the user based on this result.";
                }
                else
                {
                    _debugService.Error("Tool", $"Tool {route.ToolName} failed", toolResult.Error);
                    toolResultContext = $"The tool '{route.ToolName}' failed with error: {toolResult.Error}\n\nApologize to the user and explain what went wrong.";
                }
            }

            // ── Handle MEMORY route ────────────────────────────────────────
            if (route.RouteType == QueryRouteType.Memory || route.RouteType == QueryRouteType.Direct)
            {
                var memSettings = await _settingsService.GetMemorySettingsAsync();

                if (memSettings.IsEnabled && _memoryService.IsReady)
                {
                    StatusMessage = "Searching memories...";
                    _debugService.Info("Memory", "Searching for relevant memories");

                    var memories = await _memoryService.SearchMemoriesAsync(
                        lastUserMsg,
                        sessionId: null,
                        topK: memSettings.MaxMemoriesToInject,
                        similarityThreshold: memSettings.SimilarityThreshold,
                        cancellationToken: _streamingCts.Token);

                    if (memories.Count > 0)
                    {
                        _debugService.Success("Memory", $"Found {memories.Count} relevant memories");

                        var sb = new StringBuilder();
                        sb.AppendLine("## REMEMBERED INFORMATION");
                        sb.AppendLine("Use this information from past conversations to answer:");
                        sb.AppendLine();
                        foreach (var mem in memories)
                        {
                            var roleName = mem.Entry.Role == MessageRole.User ? "User" : "Assistant";
                            var timeAgo = GetTimeAgo(mem.Entry.CreatedAt);
                            sb.AppendLine($"- [{timeAgo}] {roleName}: \"{mem.Entry.Content}\"");
                        }
                        memoryContext = sb.ToString();
                    }
                    else
                    {
                        _debugService.Info("Memory", "No relevant memories found");
                    }
                }
            }

            // ── Handle AGENT KNOWLEDGE search ─────────────────────────────
            string? agentKnowledgeContext = null;
            if (SelectedAgent != null && UseAgentKnowledge)
            {
                StatusMessage = $"Searching {SelectedAgent.Name} knowledge...";
                _debugService.Info("AgentKnowledge", $"Searching agent: {SelectedAgent.Name}",
                    $"Query: {lastUserMsg}");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var searchResults = await _agentService.SearchKnowledgeAsync(
                    SelectedAgent.Id,
                    lastUserMsg,
                    maxResults: 5);
                var searchTime = stopwatch.ElapsedMilliseconds;

                if (searchResults.Count > 0)
                {
                    _debugService.Success("AgentKnowledge",
                        $"Found {searchResults.Count} relevant chunks from {SelectedAgent.Name}",
                        $"Time: {searchTime}ms | Best match: {searchResults[0].Similarity:F3}");

                    var sb = new StringBuilder();
                    sb.AppendLine($"## KNOWLEDGE FROM: {SelectedAgent.Name.ToUpper()}");
                    if (!string.IsNullOrEmpty(SelectedAgent.Description))
                    {
                        sb.AppendLine($"Agent description: {SelectedAgent.Description}");
                    }
                    sb.AppendLine();
                    sb.AppendLine("Use this knowledge to answer the user's question:");
                    sb.AppendLine();

                    foreach (var result in searchResults)
                    {
                        sb.AppendLine($"[Source: {result.DocumentName}]");
                        sb.AppendLine(result.Chunk.Content);
                        sb.AppendLine();
                    }

                    agentKnowledgeContext = sb.ToString();
                }
                else
                {
                    _debugService.Warning("AgentKnowledge",
                        $"No relevant knowledge found in {SelectedAgent.Name}",
                        $"Time: {searchTime}ms");
                }
            }

            // ═══════════════════════════════════════════════════════════════
            // BUILD SYSTEM PROMPT (clean, no tool instructions)
            // ═══════════════════════════════════════════════════════════════
            var systemPromptParts = new List<string>();

            // Base system prompt
            if (!string.IsNullOrEmpty(settings.SystemPrompt))
            {
                systemPromptParts.Add(settings.SystemPrompt);
            }

            // Add agent's custom system prompt if selected
            if (SelectedAgent != null && !string.IsNullOrEmpty(SelectedAgent.SystemPrompt))
            {
                systemPromptParts.Add($"## AGENT INSTRUCTIONS\n{SelectedAgent.SystemPrompt}");
            }

            // Add agent knowledge context (highest priority for answers)
            if (!string.IsNullOrEmpty(agentKnowledgeContext))
            {
                systemPromptParts.Add(agentKnowledgeContext);
                systemPromptParts.Add("IMPORTANT: Base your answer primarily on the knowledge provided above. If the answer isn't in the knowledge, say so.");
            }

            // Add tool result context if we executed a tool
            if (!string.IsNullOrEmpty(toolResultContext))
            {
                systemPromptParts.Add(toolResultContext);
            }

            // Add memory context
            if (!string.IsNullOrEmpty(memoryContext))
            {
                systemPromptParts.Add(memoryContext);
            }

            // Check for context references (short messages like "yes", "ok")
            var lastUserMessages = Messages.Where(m => m.Role == MessageRole.User).TakeLast(3).ToList();
            if (lastUserMessages.Any(m => IsContextReferenceMessage(m.Content)))
            {
                systemPromptParts.Insert(0, "IMPORTANT: The user's message refers to the previous conversation. Consider the full context.");
            }

            settings.SystemPrompt = string.Join("\n\n", systemPromptParts);
            // ═══════════════════════════════════════════════════════════════

            // Build chat messages
            var chatMessages = Messages
                .Where(m => !m.IsStreaming || m == messageVm)
                .Select(m => new ChatMessage { Role = m.Role, Content = m.Content })
                .SkipLast(1)
                .ToList();

            _debugService.Info("LLM", $"Sending to LLM with {chatMessages.Count} messages",
                $"System prompt: {settings.SystemPrompt?.Length ?? 0} chars");

            // Verify Ollama connection
            if (SelectedProvider == LLMProviderType.Ollama)
            {
                StatusMessage = "Connecting to Ollama...";
                if (!await service.ValidateConnectionAsync(_streamingCts.Token))
                {
                    throw new InvalidOperationException("Cannot connect to Ollama. Make sure Ollama is running.");
                }
            }

            // Stream the response
            StatusMessage = $"Waiting for {SelectedModel}...";
            var responseBuilder = new StringBuilder();
            var tokenCount = 0;
            var startTime = DateTime.Now;

            await foreach (var chunk in service.StreamMessageAsync(chatMessages, settings, _streamingCts.Token))
            {
                responseBuilder.Append(chunk);
                tokenCount++;

                if (tokenCount == 1) StatusMessage = "Generating response...";

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    messageVm.Content = responseBuilder.ToString();
                });
            }

            var fullResponse = responseBuilder.ToString();
            var elapsed = DateTime.Now - startTime;

            // Save the message
            assistantMessage.Content = fullResponse;
            assistantMessage.IsStreaming = false;

            Application.Current.Dispatcher.Invoke(() => messageVm.IsStreaming = false);

            _debugService.Success("LLM", $"Response complete",
                $"Tokens: ~{tokenCount}, Time: {elapsed.TotalSeconds:F2}s");

            await _chatRepository.AddMessageAsync(assistantMessage);
            StatusMessage = IsMemoryEnabled ? $"Ready · {MemoryCount} memories" : "Ready";

            // Store in memory if not a tool query
            if (!isToolQuery && ShouldStoreInMemory(fullResponse, isToolQuery))
            {
                _ = StoreMessageInMemoryAsync(fullResponse, MessageRole.Assistant);
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
    private void ClearAgent()
    {
        SelectedAgent = null;
        UseAgentKnowledge = false;
        _debugService.Info("Chat", "Agent knowledge disabled");
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
