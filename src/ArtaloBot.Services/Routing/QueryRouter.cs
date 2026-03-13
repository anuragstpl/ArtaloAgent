using System.Text.RegularExpressions;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Routing;

/// <summary>
/// Routes user queries to the appropriate handler: Tool, Memory, or Direct LLM.
/// This removes the need for LLM to decide - we pre-classify and execute accordingly.
/// </summary>
public class QueryRouter
{
    private readonly IMCPService _mcpService;
    private readonly IDebugService? _debugService;

    // Tool intent patterns - maps patterns to tool names and argument extractors
    private readonly List<ToolIntent> _toolIntents;

    public QueryRouter(IMCPService mcpService, IDebugService? debugService = null)
    {
        _mcpService = mcpService;
        _debugService = debugService;
        _toolIntents = BuildToolIntents();
    }

    /// <summary>
    /// Analyzes a query and returns the routing decision.
    /// </summary>
    public QueryRouteResult Route(string query)
    {
        var lowerQuery = query.ToLowerInvariant().Trim();

        _debugService?.Info("Router", "Analyzing query", query);

        // 1. Check for memory/personal queries first (highest priority)
        if (IsMemoryQuery(lowerQuery))
        {
            _debugService?.Info("Router", "Routed to MEMORY", "Query asks about remembered information");
            return new QueryRouteResult
            {
                RouteType = QueryRouteType.Memory,
                Reason = "Query asks about personal/remembered information"
            };
        }

        // 2. Check for tool intents
        var toolMatch = MatchToolIntent(lowerQuery, query);
        if (toolMatch != null)
        {
            _debugService?.Info("Router", $"Routed to TOOL: {toolMatch.ToolName}",
                $"Arguments: {string.Join(", ", toolMatch.Arguments.Select(kv => $"{kv.Key}={kv.Value}"))}");
            return new QueryRouteResult
            {
                RouteType = QueryRouteType.Tool,
                ToolName = toolMatch.ToolName,
                ToolArguments = toolMatch.Arguments,
                Reason = $"Query matches tool pattern for {toolMatch.ToolName}"
            };
        }

        // 3. Default to direct LLM (with optional memory augmentation)
        _debugService?.Info("Router", "Routed to DIRECT", "No specific tool or memory pattern matched");
        return new QueryRouteResult
        {
            RouteType = QueryRouteType.Direct,
            Reason = "General query - will use LLM directly"
        };
    }

    private bool IsMemoryQuery(string lowerQuery)
    {
        var memoryPatterns = new[]
        {
            // Personal information queries
            "my name", "who am i", "what's my", "what is my", "whats my",
            "do you know my", "do you remember", "did i tell you",
            "i told you", "you know my", "remember when", "remember that",
            // Past conversation references
            "we discussed", "we talked about", "earlier i said", "previously",
            "last time", "before i said", "you said", "you mentioned",
            // Explicit memory requests
            "what do you know about me", "what have i told you"
        };

        return memoryPatterns.Any(p => lowerQuery.Contains(p));
    }

    private ToolMatch? MatchToolIntent(string lowerQuery, string originalQuery)
    {
        // Get available tools from MCP
        var availableTools = _mcpService.GetAllAvailableTools();
        if (availableTools.Count == 0) return null;

        var toolNames = availableTools.Select(t => t.Tool.Name.ToLowerInvariant()).ToHashSet();

        foreach (var intent in _toolIntents)
        {
            // Check if the tool exists
            if (!toolNames.Contains(intent.ToolName.ToLowerInvariant())) continue;

            // Check if query matches any pattern
            foreach (var pattern in intent.Patterns)
            {
                var match = Regex.Match(lowerQuery, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var args = intent.ExtractArguments(originalQuery, match);
                    return new ToolMatch
                    {
                        ToolName = intent.ToolName,
                        Arguments = args
                    };
                }
            }
        }

        return null;
    }

    private List<ToolIntent> BuildToolIntents()
    {
        return new List<ToolIntent>
        {
            // Time tool
            new ToolIntent
            {
                ToolName = "get_current_time",
                Patterns = new[]
                {
                    @"(?:what(?:'s| is) the )?time (?:in |at )?(.+?)(?:\?|$)",
                    @"current time (?:in |at )?(.+?)(?:\?|$)",
                    @"what time is it(?: in (.+?))?(?:\?|$)",
                    @"tell me the time(?: in (.+?))?(?:\?|$)"
                },
                ExtractArguments = (query, match) =>
                {
                    var location = match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : "";
                    var timezone = LocationToTimezone(location);
                    return new Dictionary<string, object>
                    {
                        ["timezone"] = string.IsNullOrEmpty(timezone) ? "UTC" : timezone
                    };
                }
            },

            // Brave Search tool
            new ToolIntent
            {
                ToolName = "brave_web_search",
                Patterns = new[]
                {
                    @"search (?:for |about )?(.+?)(?:\?|$)",
                    @"look up (.+?)(?:\?|$)",
                    @"find (?:information (?:about |on )?)?(.+?)(?:\?|$)",
                    @"google (.+?)(?:\?|$)",
                    @"what is (.+?) according to",
                    @"latest news (?:about |on )?(.+?)(?:\?|$)"
                },
                ExtractArguments = (query, match) =>
                {
                    var searchQuery = match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : query;
                    return new Dictionary<string, object>
                    {
                        ["query"] = searchQuery,
                        ["count"] = 5
                    };
                }
            },

            // Fetch URL tool
            new ToolIntent
            {
                ToolName = "fetch",
                Patterns = new[]
                {
                    @"(?:fetch|get|read|open) (?:the )?(?:url |page |website |content (?:of |from )?)?(?:at )?(https?://[^\s]+)",
                    @"what(?:'s| is) (?:on|at) (https?://[^\s]+)",
                    @"summarize (https?://[^\s]+)"
                },
                ExtractArguments = (query, match) =>
                {
                    var url = match.Groups[1].Value.Trim();
                    return new Dictionary<string, object>
                    {
                        ["url"] = url
                    };
                }
            },

            // File system tools
            new ToolIntent
            {
                ToolName = "read_file",
                Patterns = new[]
                {
                    @"read (?:the )?(?:file |contents of )?[""']?([^""']+)[""']?",
                    @"show (?:me )?(?:the )?(?:file |contents of )?[""']?([^""']+)[""']?",
                    @"what(?:'s| is) in (?:the file )?[""']?([^""']+)[""']?"
                },
                ExtractArguments = (query, match) =>
                {
                    var path = match.Groups[1].Value.Trim();
                    return new Dictionary<string, object>
                    {
                        ["path"] = path
                    };
                }
            },

            new ToolIntent
            {
                ToolName = "list_directory",
                Patterns = new[]
                {
                    @"list (?:the )?(?:files|directory|folder|contents)(?: (?:in|of) )?[""']?([^""']*)[""']?",
                    @"show (?:me )?(?:the )?(?:files|directory|folder)(?: (?:in|of) )?[""']?([^""']*)[""']?",
                    @"what(?:'s| is) in (?:the )?(?:folder|directory) [""']?([^""']+)[""']?"
                },
                ExtractArguments = (query, match) =>
                {
                    var path = match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : ".";
                    return new Dictionary<string, object>
                    {
                        ["path"] = string.IsNullOrEmpty(path) ? "." : path
                    };
                }
            }
        };
    }

    private static string LocationToTimezone(string location)
    {
        if (string.IsNullOrWhiteSpace(location)) return "";

        var lower = location.ToLowerInvariant().Trim();

        // Common city/region to timezone mapping
        var timezoneMap = new Dictionary<string, string>
        {
            // Asia
            ["tokyo"] = "Asia/Tokyo",
            ["japan"] = "Asia/Tokyo",
            ["singapore"] = "Asia/Singapore",
            ["hong kong"] = "Asia/Hong_Kong",
            ["hongkong"] = "Asia/Hong_Kong",
            ["shanghai"] = "Asia/Shanghai",
            ["beijing"] = "Asia/Shanghai",
            ["china"] = "Asia/Shanghai",
            ["seoul"] = "Asia/Seoul",
            ["korea"] = "Asia/Seoul",
            ["mumbai"] = "Asia/Kolkata",
            ["delhi"] = "Asia/Kolkata",
            ["india"] = "Asia/Kolkata",
            ["bangkok"] = "Asia/Bangkok",
            ["thailand"] = "Asia/Bangkok",
            ["dubai"] = "Asia/Dubai",
            ["uae"] = "Asia/Dubai",

            // Europe
            ["london"] = "Europe/London",
            ["uk"] = "Europe/London",
            ["paris"] = "Europe/Paris",
            ["france"] = "Europe/Paris",
            ["berlin"] = "Europe/Berlin",
            ["germany"] = "Europe/Berlin",
            ["amsterdam"] = "Europe/Amsterdam",
            ["moscow"] = "Europe/Moscow",
            ["russia"] = "Europe/Moscow",

            // Americas
            ["new york"] = "America/New_York",
            ["nyc"] = "America/New_York",
            ["los angeles"] = "America/Los_Angeles",
            ["la"] = "America/Los_Angeles",
            ["chicago"] = "America/Chicago",
            ["toronto"] = "America/Toronto",
            ["vancouver"] = "America/Vancouver",
            ["sao paulo"] = "America/Sao_Paulo",
            ["brazil"] = "America/Sao_Paulo",

            // Australia
            ["sydney"] = "Australia/Sydney",
            ["melbourne"] = "Australia/Melbourne",
            ["australia"] = "Australia/Sydney",

            // General
            ["utc"] = "UTC",
            ["gmt"] = "UTC"
        };

        foreach (var (key, tz) in timezoneMap)
        {
            if (lower.Contains(key)) return tz;
        }

        // Return as-is, might be a valid timezone already
        return location;
    }
}

public enum QueryRouteType
{
    Tool,    // Call an MCP tool
    Memory,  // Search vector memory
    Direct   // Send directly to LLM
}

public class QueryRouteResult
{
    public QueryRouteType RouteType { get; set; }
    public string? ToolName { get; set; }
    public Dictionary<string, object>? ToolArguments { get; set; }
    public string Reason { get; set; } = "";
}

public class ToolIntent
{
    public string ToolName { get; set; } = "";
    public string[] Patterns { get; set; } = [];
    public Func<string, Match, Dictionary<string, object>> ExtractArguments { get; set; } = (_, _) => new();
}

public class ToolMatch
{
    public string ToolName { get; set; } = "";
    public Dictionary<string, object> Arguments { get; set; } = new();
}
