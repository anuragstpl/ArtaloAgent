using System.Text.Json;

namespace ArtaloBot.Core.Models;

/// <summary>
/// Represents a skill attached to an agent with its configuration.
/// </summary>
public class AgentSkill
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public SkillType SkillType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = "{}";
    public bool IsEnabled { get; set; } = true;
    public bool IsTested { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Agent? Agent { get; set; }

    // Helper to get typed config
    public T? GetConfig<T>() where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(ConfigJson);
        }
        catch
        {
            return null;
        }
    }

    public void SetConfig<T>(T config) where T : class
    {
        ConfigJson = JsonSerializer.Serialize(config);
    }
}

public enum SkillType
{
    Email,
    Webhook,
    JobSchedule,
    Database,
    FileOperation
}

#region Skill Configurations

/// <summary>
/// Configuration for email sending skill.
/// </summary>
public class EmailSkillConfig
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;

    // Optional defaults
    public string DefaultToEmail { get; set; } = string.Empty;
    public string DefaultSubjectPrefix { get; set; } = string.Empty;
    public string SignatureHtml { get; set; } = string.Empty;
}

/// <summary>
/// Configuration for webhook skill.
/// </summary>
public class WebhookSkillConfig
{
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "POST"; // GET, POST, PUT, DELETE
    public Dictionary<string, string> Headers { get; set; } = new();
    public string AuthType { get; set; } = "None"; // None, Bearer, Basic, ApiKey
    public string AuthValue { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/json";
    public string BodyTemplate { get; set; } = "{}"; // Template with {{placeholders}}
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Configuration for job scheduling skill.
/// </summary>
public class JobScheduleSkillConfig
{
    public string CronExpression { get; set; } = string.Empty; // e.g., "0 9 * * *" for 9 AM daily
    public string JobType { get; set; } = "SendMessage"; // SendMessage, CallWebhook, SendEmail
    public string JobPayload { get; set; } = "{}"; // Configuration for the job
    public bool IsActive { get; set; } = false;
    public string Timezone { get; set; } = "UTC";
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
}

#endregion
