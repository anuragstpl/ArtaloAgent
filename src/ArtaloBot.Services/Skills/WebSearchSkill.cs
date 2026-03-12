using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Skills;

public class WebSearchSkill : BaseSkill
{
    private readonly HttpClient _httpClient;
    private string? _apiKey;
    private string? _searchEngineId;

    public override string SkillId => "web_search";
    public override string Name => "Web Search";
    public override string Description => "Searches the web for information";
    public override SkillCategory Category => SkillCategory.Search;
    public override IEnumerable<string> TriggerKeywords => ["search", "look up", "find", "google", "search for"];

    public WebSearchSkill(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void Configure(string apiKey, string searchEngineId)
    {
        _apiKey = apiKey;
        _searchEngineId = searchEngineId;
    }

    public override async Task<SkillExecutionResult> ExecuteAsync(
        string input,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_searchEngineId))
        {
            return Failure("Web search is not configured. Please set up Google Custom Search API key.");
        }

        try
        {
            var query = ExtractQuery(input);
            var url = $"https://www.googleapis.com/customsearch/v1?key={_apiKey}&cx={_searchEngineId}&q={Uri.EscapeDataString(query)}&num=5";

            var response = await _httpClient.GetFromJsonAsync<GoogleSearchResponse>(url, cancellationToken);

            if (response?.Items == null || response.Items.Count == 0)
            {
                return Success($"No results found for: {query}");
            }

            var results = response.Items.Select((item, index) =>
                $"{index + 1}. **{item.Title}**\n   {item.Snippet}\n   [{item.Link}]({item.Link})");

            var resultText = $"Search results for \"{query}\":\n\n{string.Join("\n\n", results)}";

            return Success(resultText, new Dictionary<string, object>
            {
                ["query"] = query,
                ["resultCount"] = response.Items.Count
            });
        }
        catch (Exception ex)
        {
            return Failure($"Search error: {ex.Message}");
        }
    }

    private static string ExtractQuery(string input)
    {
        return input
            .Replace("search for", "", StringComparison.OrdinalIgnoreCase)
            .Replace("search", "", StringComparison.OrdinalIgnoreCase)
            .Replace("look up", "", StringComparison.OrdinalIgnoreCase)
            .Replace("find", "", StringComparison.OrdinalIgnoreCase)
            .Replace("google", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private class GoogleSearchResponse
    {
        [JsonPropertyName("items")]
        public List<SearchItem>? Items { get; set; }
    }

    private class SearchItem
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("link")]
        public string Link { get; set; } = string.Empty;

        [JsonPropertyName("snippet")]
        public string Snippet { get; set; } = string.Empty;
    }
}
