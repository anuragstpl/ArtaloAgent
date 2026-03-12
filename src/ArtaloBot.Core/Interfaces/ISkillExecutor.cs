using ArtaloBot.Core.Models;

namespace ArtaloBot.Core.Interfaces;

public interface ISkillExecutor
{
    string SkillId { get; }
    string Name { get; }
    string Description { get; }
    SkillCategory Category { get; }
    IEnumerable<string> TriggerKeywords { get; }

    Task<SkillExecutionResult> ExecuteAsync(
        string input,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default);

    bool CanHandle(string input);
}

public interface ISkillRegistry
{
    void Register(ISkillExecutor skill);
    void Unregister(string skillId);
    ISkillExecutor? GetSkill(string skillId);
    IEnumerable<ISkillExecutor> GetAllSkills();
    ISkillExecutor? FindSkillForInput(string input);
}
