using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Skills;

public class SkillRegistry : ISkillRegistry
{
    private readonly Dictionary<string, ISkillExecutor> _skills = [];

    public void Register(ISkillExecutor skill)
    {
        _skills[skill.SkillId] = skill;
    }

    public void Unregister(string skillId)
    {
        _skills.Remove(skillId);
    }

    public ISkillExecutor? GetSkill(string skillId)
    {
        return _skills.TryGetValue(skillId, out var skill) ? skill : null;
    }

    public IEnumerable<ISkillExecutor> GetAllSkills()
    {
        return _skills.Values;
    }

    public ISkillExecutor? FindSkillForInput(string input)
    {
        var lowerInput = input.ToLowerInvariant();
        return _skills.Values.FirstOrDefault(s => s.CanHandle(lowerInput));
    }
}
