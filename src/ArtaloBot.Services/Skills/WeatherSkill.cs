using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Skills;

public class WeatherSkill : BaseSkill
{
    private readonly HttpClient _httpClient;
    private string? _apiKey;

    public override string SkillId => "weather";
    public override string Name => "Weather";
    public override string Description => "Gets current weather information for a location";
    public override SkillCategory Category => SkillCategory.Utility;
    public override IEnumerable<string> TriggerKeywords =>
        ["weather", "temperature", "forecast", "how hot", "how cold", "is it raining"];

    public WeatherSkill(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void Configure(string apiKey)
    {
        _apiKey = apiKey;
    }

    public override async Task<SkillExecutionResult> ExecuteAsync(
        string input,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return Failure("Weather service is not configured. Please set up OpenWeatherMap API key.");
        }

        try
        {
            var location = ExtractLocation(input);
            if (string.IsNullOrWhiteSpace(location))
            {
                location = "London"; // Default location
            }

            var url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(location)}&appid={_apiKey}&units=metric";
            var response = await _httpClient.GetFromJsonAsync<WeatherResponse>(url, cancellationToken);

            if (response == null)
            {
                return Failure($"Could not get weather for: {location}");
            }

            var result = $"Weather in {response.Name}, {response.Sys?.Country}:\n\n" +
                         $"Temperature: {response.Main?.Temp:F1}°C (feels like {response.Main?.FeelsLike:F1}°C)\n" +
                         $"Condition: {response.Weather?.FirstOrDefault()?.Description}\n" +
                         $"Humidity: {response.Main?.Humidity}%\n" +
                         $"Wind: {response.Wind?.Speed} m/s\n" +
                         $"Pressure: {response.Main?.Pressure} hPa";

            return Success(result, new Dictionary<string, object>
            {
                ["location"] = location,
                ["temperature"] = response.Main?.Temp ?? 0,
                ["condition"] = response.Weather?.FirstOrDefault()?.Main ?? "Unknown"
            });
        }
        catch (Exception ex)
        {
            return Failure($"Weather error: {ex.Message}");
        }
    }

    private static string ExtractLocation(string input)
    {
        // Remove common weather keywords and extract location
        var location = input
            .Replace("weather in", "", StringComparison.OrdinalIgnoreCase)
            .Replace("weather for", "", StringComparison.OrdinalIgnoreCase)
            .Replace("weather", "", StringComparison.OrdinalIgnoreCase)
            .Replace("temperature in", "", StringComparison.OrdinalIgnoreCase)
            .Replace("forecast for", "", StringComparison.OrdinalIgnoreCase)
            .Replace("what's the", "", StringComparison.OrdinalIgnoreCase)
            .Replace("how's the", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return location;
    }

    #region DTOs

    private class WeatherResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("main")]
        public MainData? Main { get; set; }

        [JsonPropertyName("weather")]
        public List<WeatherData>? Weather { get; set; }

        [JsonPropertyName("wind")]
        public WindData? Wind { get; set; }

        [JsonPropertyName("sys")]
        public SysData? Sys { get; set; }
    }

    private class MainData
    {
        [JsonPropertyName("temp")]
        public double Temp { get; set; }

        [JsonPropertyName("feels_like")]
        public double FeelsLike { get; set; }

        [JsonPropertyName("humidity")]
        public int Humidity { get; set; }

        [JsonPropertyName("pressure")]
        public int Pressure { get; set; }
    }

    private class WeatherData
    {
        [JsonPropertyName("main")]
        public string Main { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
    }

    private class WindData
    {
        [JsonPropertyName("speed")]
        public double Speed { get; set; }
    }

    private class SysData
    {
        [JsonPropertyName("country")]
        public string Country { get; set; } = string.Empty;
    }

    #endregion
}
