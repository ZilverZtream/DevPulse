using DevPulse.Core.Models;
using Serilog;

namespace DevPulse.Core.Services;

public sealed class AiTemplateRenderer
{
    private static readonly HashSet<string> AllowedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "description", "areaPath", "iterationPath", "type", "state", "acceptanceCriteria"
    };

    public string Render(string templateBody, WorkItem workItem, string description, string acceptanceCriteria)
    {
        if (string.IsNullOrEmpty(templateBody)) return string.Empty;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = workItem.Title ?? string.Empty,
            ["description"] = description ?? string.Empty,
            ["areaPath"] = workItem.AreaPath ?? string.Empty,
            ["iterationPath"] = workItem.IterationPath ?? string.Empty,
            ["type"] = workItem.Type.ToString(),
            ["state"] = workItem.State ?? string.Empty,
            ["acceptanceCriteria"] = acceptanceCriteria ?? string.Empty,
        };

        return System.Text.RegularExpressions.Regex.Replace(
            templateBody,
            @"\{([a-zA-Z]+)\}",
            match =>
            {
                var tokenName = match.Groups[1].Value;
                if (values.TryGetValue(tokenName, out var value))
                    return value;
                if (!AllowedTokens.Contains(tokenName))
                    Log.Warning("AiTemplateRenderer: unknown token '{Token}' left literal", tokenName);
                return match.Value;
            });
    }
}
