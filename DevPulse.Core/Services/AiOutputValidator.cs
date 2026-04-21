using System.Text.RegularExpressions;
using DevPulse.Core.Models;

namespace DevPulse.Core.Services;

public sealed class AiOutputValidator
{
    private static readonly Regex H2Regex = new(@"^##\s+(.+?)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public AiValidationResult Validate(string markdown, IReadOnlyList<string> requiredHeaders)
    {
        var result = new AiValidationResult { IsValid = true };
        if (string.IsNullOrEmpty(markdown))
        {
            result.IsValid = false;
            result.MissingHeaders = [.. requiredHeaders];
            return result;
        }

        var matches = H2Regex.Matches(markdown);
        var presentHeaders = matches
            .Select(m => (Name: m.Groups[1].Value.Trim(), Index: m.Index, EndIndex: m.Index + m.Length))
            .ToList();

        foreach (var required in requiredHeaders)
        {
            var match = presentHeaders.FirstOrDefault(p =>
                p.Name.Equals(required, StringComparison.OrdinalIgnoreCase));
            if (match == default)
            {
                result.MissingHeaders.Add(required);
                result.IsValid = false;
                continue;
            }

            var thisIdx = presentHeaders.FindIndex(p => p.Index == match.Index);
            var bodyStart = match.EndIndex;
            var bodyEnd = thisIdx + 1 < presentHeaders.Count
                ? presentHeaders[thisIdx + 1].Index
                : markdown.Length;
            var body = markdown[bodyStart..bodyEnd];
            if (string.IsNullOrWhiteSpace(body))
            {
                result.EmptySections.Add(required);
                result.IsValid = false;
            }
        }

        return result;
    }
}
