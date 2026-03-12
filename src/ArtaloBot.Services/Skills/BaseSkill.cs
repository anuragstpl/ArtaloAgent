using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Skills;

public abstract class BaseSkill : ISkillExecutor
{
    public abstract string SkillId { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract SkillCategory Category { get; }
    public abstract IEnumerable<string> TriggerKeywords { get; }

    public virtual bool CanHandle(string input)
    {
        var lowerInput = input.ToLowerInvariant();
        return TriggerKeywords.Any(keyword => lowerInput.Contains(keyword.ToLowerInvariant()));
    }

    public abstract Task<SkillExecutionResult> ExecuteAsync(
        string input,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default);

    protected static SkillExecutionResult Success(string result, Dictionary<string, object>? metadata = null)
    {
        return new SkillExecutionResult
        {
            Success = true,
            Result = result,
            Metadata = metadata
        };
    }

    protected static SkillExecutionResult Failure(string error)
    {
        return new SkillExecutionResult
        {
            Success = false,
            Error = error
        };
    }
}
