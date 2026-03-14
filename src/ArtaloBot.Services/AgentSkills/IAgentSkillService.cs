using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.AgentSkills;

/// <summary>
/// Service for managing and executing agent skills.
/// </summary>
public interface IAgentSkillService
{
    // CRUD operations
    Task<List<AgentSkill>> GetSkillsForAgentAsync(int agentId);
    Task<AgentSkill?> GetSkillAsync(int skillId);
    Task<AgentSkill> CreateSkillAsync(AgentSkill skill);
    Task<AgentSkill> UpdateSkillAsync(AgentSkill skill);
    Task DeleteSkillAsync(int skillId);

    // Execution
    Task<SkillExecutionResult> ExecuteSkillAsync(int skillId, Dictionary<string, object>? parameters = null);
    Task<SkillExecutionResult> TestSkillAsync(AgentSkill skill, Dictionary<string, string>? testParams = null);

    // Skill detection from message
    Task<List<AgentSkill>> DetectApplicableSkillsAsync(int agentId, string userMessage);
}
