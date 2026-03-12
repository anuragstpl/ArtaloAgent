using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Skills;

public class DateTimeSkill : BaseSkill
{
    public override string SkillId => "datetime";
    public override string Name => "Date & Time";
    public override string Description => "Provides current date, time, and timezone information";
    public override SkillCategory Category => SkillCategory.Utility;
    public override IEnumerable<string> TriggerKeywords =>
        ["what time", "current time", "what date", "today's date", "what day", "timezone"];

    public override Task<SkillExecutionResult> ExecuteAsync(
        string input,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;
        var timezone = TimeZoneInfo.Local;

        var lowerInput = input.ToLowerInvariant();

        string result;

        if (lowerInput.Contains("time"))
        {
            result = $"Current time: {now:HH:mm:ss}\n" +
                     $"UTC time: {utcNow:HH:mm:ss}\n" +
                     $"Timezone: {timezone.DisplayName}";
        }
        else if (lowerInput.Contains("date") || lowerInput.Contains("day"))
        {
            result = $"Today's date: {now:dddd, MMMM d, yyyy}\n" +
                     $"Day of week: {now.DayOfWeek}\n" +
                     $"Day of year: {now.DayOfYear}\n" +
                     $"Week number: {GetWeekOfYear(now)}";
        }
        else
        {
            result = $"Current date and time:\n" +
                     $"Date: {now:dddd, MMMM d, yyyy}\n" +
                     $"Time: {now:HH:mm:ss}\n" +
                     $"UTC: {utcNow:yyyy-MM-dd HH:mm:ss}\n" +
                     $"Timezone: {timezone.DisplayName}";
        }

        return Task.FromResult(Success(result, new Dictionary<string, object>
        {
            ["localTime"] = now.ToString("O"),
            ["utcTime"] = utcNow.ToString("O"),
            ["timezone"] = timezone.Id
        }));
    }

    private static int GetWeekOfYear(DateTime date)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        return culture.Calendar.GetWeekOfYear(date,
            System.Globalization.CalendarWeekRule.FirstFourDayWeek,
            DayOfWeek.Monday);
    }
}
