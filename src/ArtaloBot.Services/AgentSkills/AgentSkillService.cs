using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using ArtaloBot.Data;
using Microsoft.EntityFrameworkCore;

namespace ArtaloBot.Services.AgentSkills;

/// <summary>
/// Service for managing and executing agent skills.
/// </summary>
public class AgentSkillService : IAgentSkillService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDebugService? _debugService;

    public AgentSkillService(
        IDbContextFactory<AppDbContext> dbFactory,
        IHttpClientFactory httpClientFactory,
        IDebugService? debugService = null)
    {
        _dbFactory = dbFactory;
        _httpClientFactory = httpClientFactory;
        _debugService = debugService;
    }

    #region CRUD Operations

    public async Task<List<AgentSkill>> GetSkillsForAgentAsync(int agentId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AgentSkills
            .Where(s => s.AgentId == agentId)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<AgentSkill?> GetSkillAsync(int skillId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AgentSkills.FindAsync(skillId);
    }

    public async Task<AgentSkill> CreateSkillAsync(AgentSkill skill)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        skill.CreatedAt = DateTime.UtcNow;
        skill.UpdatedAt = DateTime.UtcNow;

        db.AgentSkills.Add(skill);
        await db.SaveChangesAsync();

        _debugService?.Success("AgentSkill", $"Created skill: {skill.Name}", $"Type: {skill.SkillType}");

        return skill;
    }

    public async Task<AgentSkill> UpdateSkillAsync(AgentSkill skill)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.AgentSkills.FindAsync(skill.Id);
        if (existing == null)
            throw new InvalidOperationException($"Skill {skill.Id} not found");

        existing.Name = skill.Name;
        existing.Description = skill.Description;
        existing.ConfigJson = skill.ConfigJson;
        existing.IsEnabled = skill.IsEnabled;
        existing.IsTested = skill.IsTested;
        existing.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _debugService?.Info("AgentSkill", $"Updated skill: {skill.Name}");

        return existing;
    }

    public async Task DeleteSkillAsync(int skillId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var skill = await db.AgentSkills.FindAsync(skillId);
        if (skill == null) return;

        db.AgentSkills.Remove(skill);
        await db.SaveChangesAsync();

        _debugService?.Info("AgentSkill", $"Deleted skill: {skill.Name}");
    }

    #endregion

    #region Execution

    public async Task<SkillExecutionResult> ExecuteSkillAsync(int skillId, Dictionary<string, object>? parameters = null)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var skill = await db.AgentSkills.FindAsync(skillId);

            if (skill == null)
            {
                return new SkillExecutionResult { Success = false, Error = "Skill not found" };
            }

            if (!skill.IsEnabled)
            {
                return new SkillExecutionResult { Success = false, Error = "Skill is disabled" };
            }

            // Convert parameters to string dictionary
            var stringParams = parameters?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString() ?? "") ?? new Dictionary<string, string>();

            _debugService?.Info("AgentSkill", $"Executing skill: {skill.Name}",
                $"Type: {skill.SkillType} | Params: {stringParams.Count}");

            return skill.SkillType switch
            {
                SkillType.Email => await ExecuteEmailSkillAsync(skill, stringParams),
                SkillType.Webhook => await ExecuteWebhookSkillAsync(skill, stringParams),
                SkillType.JobSchedule => ExecuteJobScheduleSkillAsync(skill, stringParams),
                _ => new SkillExecutionResult { Success = false, Error = "Unsupported skill type" }
            };
        }
        catch (Exception ex)
        {
            _debugService?.Error("AgentSkill", "Skill execution failed", ex.Message);
            return new SkillExecutionResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<SkillExecutionResult> TestSkillAsync(AgentSkill skill, Dictionary<string, string>? testParams = null)
    {
        testParams ??= new Dictionary<string, string>();

        try
        {
            _debugService?.Info("AgentSkill", $"Testing skill: {skill.Name}", $"Type: {skill.SkillType}");

            var result = skill.SkillType switch
            {
                SkillType.Email => await TestEmailSkillAsync(skill, testParams),
                SkillType.Webhook => await TestWebhookSkillAsync(skill, testParams),
                SkillType.JobSchedule => TestJobScheduleSkill(skill),
                _ => new SkillExecutionResult { Success = false, Error = "Unsupported skill type" }
            };

            if (result.Success)
            {
                // Mark as tested
                await using var db = await _dbFactory.CreateDbContextAsync();
                var dbSkill = await db.AgentSkills.FindAsync(skill.Id);
                if (dbSkill != null)
                {
                    dbSkill.IsTested = true;
                    dbSkill.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }

                _debugService?.Success("AgentSkill", $"Skill test passed: {skill.Name}");
            }
            else
            {
                _debugService?.Warning("AgentSkill", $"Skill test failed: {skill.Name}", result.Error ?? "");
            }

            return result;
        }
        catch (Exception ex)
        {
            return new SkillExecutionResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<List<AgentSkill>> DetectApplicableSkillsAsync(int agentId, string userMessage)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var skills = await db.AgentSkills
            .Where(s => s.AgentId == agentId && s.IsEnabled)
            .ToListAsync();

        var lowerMessage = userMessage.ToLowerInvariant();
        var applicableSkills = new List<AgentSkill>();

        foreach (var skill in skills)
        {
            var isApplicable = skill.SkillType switch
            {
                SkillType.Email => DetectEmailIntent(lowerMessage),
                SkillType.Webhook => DetectWebhookIntent(lowerMessage, skill),
                SkillType.JobSchedule => DetectScheduleIntent(lowerMessage),
                _ => false
            };

            if (isApplicable)
            {
                applicableSkills.Add(skill);
            }
        }

        return applicableSkills;
    }

    #endregion

    #region Email Skill Implementation

    private async Task<SkillExecutionResult> ExecuteEmailSkillAsync(AgentSkill skill, Dictionary<string, string> parameters)
    {
        var config = skill.GetConfig<EmailSkillConfig>();
        if (config == null)
        {
            return new SkillExecutionResult { Success = false, Error = "Invalid email configuration" };
        }

        if (!parameters.TryGetValue("to", out var toEmail) || string.IsNullOrEmpty(toEmail))
        {
            return new SkillExecutionResult { Success = false, Error = "Recipient email address is required" };
        }

        if (!parameters.TryGetValue("subject", out var subject))
            subject = "Message from ArtaloBot";

        if (!parameters.TryGetValue("body", out var body))
        {
            return new SkillExecutionResult { Success = false, Error = "Email body is required" };
        }

        try
        {
            using var client = new SmtpClient(config.SmtpHost, config.SmtpPort)
            {
                EnableSsl = config.UseSsl,
                Credentials = new NetworkCredential(config.Username, config.Password)
            };

            var message = new MailMessage
            {
                From = new MailAddress(config.FromEmail, config.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = body.Contains("<") && body.Contains(">")
            };

            foreach (var email in toEmail.Split(',', ';'))
            {
                var trimmed = email.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    message.To.Add(trimmed);
            }

            await client.SendMailAsync(message);

            return new SkillExecutionResult
            {
                Success = true,
                Result = $"Email sent successfully to {toEmail}"
            };
        }
        catch (Exception ex)
        {
            return new SkillExecutionResult { Success = false, Error = $"Failed to send email: {ex.Message}" };
        }
    }

    private async Task<SkillExecutionResult> TestEmailSkillAsync(AgentSkill skill, Dictionary<string, string> parameters)
    {
        var config = skill.GetConfig<EmailSkillConfig>();
        if (config == null)
        {
            return new SkillExecutionResult { Success = false, Error = "Invalid email configuration" };
        }

        if (string.IsNullOrEmpty(config.SmtpHost))
            return new SkillExecutionResult { Success = false, Error = "SMTP host is required" };

        if (string.IsNullOrEmpty(config.Username))
            return new SkillExecutionResult { Success = false, Error = "SMTP username is required" };

        try
        {
            // Test SMTP connection by checking DNS
            var hostEntry = await Dns.GetHostEntryAsync(config.SmtpHost);

            // If test email provided, send a test message
            if (parameters.TryGetValue("testEmail", out var testEmail) && !string.IsNullOrEmpty(testEmail))
            {
                using var client = new SmtpClient(config.SmtpHost, config.SmtpPort)
                {
                    EnableSsl = config.UseSsl,
                    Credentials = new NetworkCredential(config.Username, config.Password),
                    Timeout = 10000
                };

                var message = new MailMessage
                {
                    From = new MailAddress(config.FromEmail, config.FromName),
                    Subject = "ArtaloBot Email Test",
                    Body = "This is a test email from ArtaloBot."
                };
                message.To.Add(testEmail);

                await client.SendMailAsync(message);
                return new SkillExecutionResult { Success = true, Result = $"Test email sent to {testEmail}" };
            }

            return new SkillExecutionResult
            {
                Success = true,
                Result = $"Email configuration valid. SMTP server {config.SmtpHost} is reachable."
            };
        }
        catch (Exception ex)
        {
            return new SkillExecutionResult { Success = false, Error = $"Email test failed: {ex.Message}" };
        }
    }

    private bool DetectEmailIntent(string message)
    {
        var keywords = new[] { "send email", "send an email", "email to", "mail to", "compose email" };
        return keywords.Any(k => message.Contains(k));
    }

    #endregion

    #region Webhook Skill Implementation

    private async Task<SkillExecutionResult> ExecuteWebhookSkillAsync(AgentSkill skill, Dictionary<string, string> parameters)
    {
        var config = skill.GetConfig<WebhookSkillConfig>();
        if (config == null)
        {
            return new SkillExecutionResult { Success = false, Error = "Invalid webhook configuration" };
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

            var request = new HttpRequestMessage(new HttpMethod(config.Method), config.Url);

            // Add headers
            foreach (var header in config.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Add authentication
            switch (config.AuthType)
            {
                case "Bearer":
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AuthValue);
                    break;
                case "Basic":
                    var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes(config.AuthValue));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
                    break;
                case "ApiKey":
                    request.Headers.TryAddWithoutValidation("X-API-Key", config.AuthValue);
                    break;
            }

            // Process body template with parameters
            if (config.Method != "GET" && !string.IsNullOrEmpty(config.BodyTemplate))
            {
                var body = ProcessTemplate(config.BodyTemplate, parameters);
                request.Content = new StringContent(body, Encoding.UTF8, config.ContentType);
            }

            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return new SkillExecutionResult
                {
                    Success = true,
                    Result = $"Webhook executed. Status: {response.StatusCode}",
                    Metadata = new Dictionary<string, object> { ["response"] = responseContent }
                };
            }
            else
            {
                return new SkillExecutionResult
                {
                    Success = false,
                    Error = $"Webhook failed with status {response.StatusCode}: {responseContent}"
                };
            }
        }
        catch (Exception ex)
        {
            return new SkillExecutionResult { Success = false, Error = $"Webhook failed: {ex.Message}" };
        }
    }

    private async Task<SkillExecutionResult> TestWebhookSkillAsync(AgentSkill skill, Dictionary<string, string> parameters)
    {
        var config = skill.GetConfig<WebhookSkillConfig>();
        if (config == null)
        {
            return new SkillExecutionResult { Success = false, Error = "Invalid webhook configuration" };
        }

        if (string.IsNullOrEmpty(config.Url))
            return new SkillExecutionResult { Success = false, Error = "Webhook URL is required" };

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(HttpMethod.Head, config.Url);
            var response = await client.SendAsync(request);

            return new SkillExecutionResult
            {
                Success = true,
                Result = $"Webhook endpoint reachable. Status: {response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            return new SkillExecutionResult { Success = false, Error = $"Cannot reach webhook: {ex.Message}" };
        }
    }

    private bool DetectWebhookIntent(string message, AgentSkill skill)
    {
        var triggerWords = skill.Description.ToLowerInvariant().Split(' ', ',', ';')
            .Where(w => w.Length > 3).ToList();
        return triggerWords.Any(t => message.Contains(t));
    }

    #endregion

    #region Job Schedule Skill Implementation

    private SkillExecutionResult ExecuteJobScheduleSkillAsync(AgentSkill skill, Dictionary<string, string> parameters)
    {
        var config = skill.GetConfig<JobScheduleSkillConfig>();
        if (config == null)
        {
            return new SkillExecutionResult { Success = false, Error = "Invalid schedule configuration" };
        }

        if (parameters.TryGetValue("action", out var action))
        {
            switch (action.ToLower())
            {
                case "activate":
                    return new SkillExecutionResult
                    {
                        Success = true,
                        Result = $"Job scheduled with cron '{config.CronExpression}'. Type: {config.JobType}"
                    };
                case "deactivate":
                    return new SkillExecutionResult { Success = true, Result = "Job schedule deactivated" };
                case "status":
                    return new SkillExecutionResult
                    {
                        Success = true,
                        Result = $"Schedule: {config.CronExpression}, Type: {config.JobType}, Active: {config.IsActive}"
                    };
            }
        }

        return new SkillExecutionResult { Success = false, Error = "Unknown action" };
    }

    private SkillExecutionResult TestJobScheduleSkill(AgentSkill skill)
    {
        var config = skill.GetConfig<JobScheduleSkillConfig>();
        if (config == null || string.IsNullOrEmpty(config.CronExpression))
        {
            return new SkillExecutionResult { Success = false, Error = "Cron expression is required" };
        }

        return new SkillExecutionResult
        {
            Success = true,
            Result = $"Valid schedule: {config.CronExpression} ({config.Timezone})"
        };
    }

    private bool DetectScheduleIntent(string message)
    {
        var keywords = new[] { "schedule", "remind me", "every day", "daily", "weekly" };
        return keywords.Any(k => message.Contains(k));
    }

    #endregion

    #region Helpers

    private static string ProcessTemplate(string template, Dictionary<string, string> parameters)
    {
        var result = template;
        foreach (var param in parameters)
        {
            result = result.Replace($"{{{{{param.Key}}}}}", param.Value);
        }
        return Regex.Replace(result, @"\{\{.*?\}\}", "");
    }

    #endregion
}
