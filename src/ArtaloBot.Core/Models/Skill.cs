namespace ArtaloBot.Core.Models;

public class Skill
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public SkillCategory Category { get; set; }
    public Dictionary<string, object> Configuration { get; set; } = [];
    public List<string> TriggerKeywords { get; set; } = [];
}

public enum SkillCategory
{
    Search,
    Productivity,
    Development,
    Communication,
    Media,
    Utility
}

public class SkillExecutionResult
{
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
