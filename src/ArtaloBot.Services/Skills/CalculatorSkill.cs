using System.Data;
using System.Text.RegularExpressions;
using ArtaloBot.Core.Models;

namespace ArtaloBot.Services.Skills;

public partial class CalculatorSkill : BaseSkill
{
    public override string SkillId => "calculator";
    public override string Name => "Calculator";
    public override string Description => "Performs mathematical calculations";
    public override SkillCategory Category => SkillCategory.Utility;
    public override IEnumerable<string> TriggerKeywords => ["calculate", "compute", "math", "what is", "evaluate"];

    [GeneratedRegex(@"[\d+\-*/().%\s^]+")]
    private static partial Regex MathExpressionRegex();

    public override Task<SkillExecutionResult> ExecuteAsync(
        string input,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract mathematical expression from input
            var expression = ExtractExpression(input);
            if (string.IsNullOrWhiteSpace(expression))
            {
                return Task.FromResult(Failure("No mathematical expression found in input"));
            }

            // Replace common math terms
            expression = expression
                .Replace("^", "**")  // Power
                .Replace("x", "*")   // Multiplication
                .Replace("X", "*");

            // Use DataTable to compute the expression
            var result = new DataTable().Compute(expression.Replace("**", "^"), null);

            return Task.FromResult(Success(
                $"Result: {result}",
                new Dictionary<string, object>
                {
                    ["expression"] = expression,
                    ["result"] = result?.ToString() ?? "null"
                }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Failure($"Calculation error: {ex.Message}"));
        }
    }

    private static string ExtractExpression(string input)
    {
        // Remove common prefixes
        var cleanInput = input
            .Replace("calculate", "", StringComparison.OrdinalIgnoreCase)
            .Replace("compute", "", StringComparison.OrdinalIgnoreCase)
            .Replace("what is", "", StringComparison.OrdinalIgnoreCase)
            .Replace("evaluate", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        var match = MathExpressionRegex().Match(cleanInput);
        return match.Success ? match.Value.Trim() : cleanInput;
    }
}
