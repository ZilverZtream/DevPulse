using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using System.Text.RegularExpressions;
using Serilog;

namespace DevPulse.Core.Services;

public sealed partial class WorkItemNormalizer
{
    [GeneratedRegex(@"AB#(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WorkItemRefRegex();

    private static readonly HashSet<int> _warnedNoUniqueName = [];
    private static readonly Dictionary<string, WorkItemType> TypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Feature"] = WorkItemType.Feature,
        ["Bug"] = WorkItemType.Bug,
        ["Task"] = WorkItemType.Task,
        ["User Story"] = WorkItemType.UserStory,
    };

    public WorkItem Normalize(WorkItemDto dto, IReadOnlyList<BoardColumnDefinition> columns, DateTimeOffset now)
    {
        DateTimeOffset stateChangedAt;
        if (dto.StateChangedDate.HasValue)
        {
            stateChangedAt = dto.StateChangedDate.Value;
        }
        else
        {
            Log.Warning("WorkItemNormalizer: missing StateChangedDate for work item {Id}, using UtcNow", dto.Id);
            stateChangedAt = now;
        }

        var days = Math.Max(0, (int)(now - stateChangedAt).TotalDays);
        var column = ResolveColumn(dto.State, columns);
        var aging = column != null ? ComputeAging(days, column) : AgingLevel.Fresh;

        if (string.IsNullOrWhiteSpace(dto.AssignedToUniqueName) && !string.IsNullOrWhiteSpace(dto.AssignedToDisplayName)
            && _warnedNoUniqueName.Add(dto.Id))
            Log.Debug("WorkItem {Id} has no UniqueName; using display name '{Name}' as canonical key — mutes may be orphaned on rename", dto.Id, dto.AssignedToDisplayName);

        return new WorkItem
        {
            Id = dto.Id,
            Title = dto.Title,
            Type = TypeMap.GetValueOrDefault(dto.WorkItemType, WorkItemType.Unknown),
            State = dto.State,
            BoardColumn = column?.Name ?? string.Empty,
            Priority = dto.Priority,
            AssignedToDisplayName = dto.AssignedToDisplayName,
            AssignedToCanonicalKey = string.IsNullOrWhiteSpace(dto.AssignedToUniqueName)
                ? dto.AssignedToDisplayName
                : dto.AssignedToUniqueName,
            AreaPath = dto.AreaPath,
            IterationPath = dto.IterationPath,
            WorkItemUrl = dto.Url,
            LinkedPullRequestId = ExtractLinkedPrId(dto.Relations),
            StateChangedAtUtc = stateChangedAt,
            DaysInCurrentState = days,
            AgingLevel = aging,
            DiscoveredAtUtc = now
        };
    }

    private static BoardColumnDefinition? ResolveColumn(string state, IReadOnlyList<BoardColumnDefinition> columns)
        => columns.FirstOrDefault(c => c.MappedStates.Any(s => s.Equals(state, StringComparison.OrdinalIgnoreCase)));

    private static AgingLevel ComputeAging(int days, BoardColumnDefinition col)
    {
        if (days >= col.AgingDaysStale) return AgingLevel.Stale;
        if (days >= col.AgingDaysWarning) return AgingLevel.Warning;
        return AgingLevel.Fresh;
    }

    private static string? ExtractLinkedPrId(List<WorkItemRelationDto> relations)
    {
        foreach (var rel in relations)
        {
            // Check vstfs:// PR link first (more specific)
            if (rel.Rel == "ArtifactLink" && rel.Url.Contains("PullRequestId", StringComparison.OrdinalIgnoreCase))
            {
                var parts = rel.Url.Split('/');
                if (parts.Length > 0 && int.TryParse(parts[^1], out _))
                    return parts[^1];
            }

            // Fall back to AB# work item reference
            var m = WorkItemRefRegex().Match(rel.Url);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }
}
