using DevPulse.Core.Enums;
using DevPulse.Core.Models;

namespace DevPulse.Core.Services;

public sealed class BoardViewService
{
    public void RecomputeAging(IReadOnlyList<WorkItem> items, IReadOnlyList<BoardColumnDefinition> columns)
    {
        foreach (var item in items)
        {
            var col = columns.FirstOrDefault(c => c.MappedStates.Any(s => s.Equals(item.State, StringComparison.OrdinalIgnoreCase)));
            if (col == null) { item.AgingLevel = AgingLevel.Fresh; continue; }
            item.AgingLevel = item.DaysInCurrentState >= col.AgingDaysStale ? AgingLevel.Stale
                            : item.DaysInCurrentState >= col.AgingDaysWarning ? AgingLevel.Warning
                            : AgingLevel.Fresh;
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<WorkItem>> GroupByColumn(
        IReadOnlyList<WorkItem> items,
        IReadOnlyList<BoardColumnDefinition> columns)
    {
        var result = new Dictionary<string, IReadOnlyList<WorkItem>>();
        foreach (var col in columns.OrderBy(c => c.Order))
        {
            result[col.Name] = items
                .Where(i => i.BoardColumn.Equals(col.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.DaysInCurrentState)
                .ToList();
        }
        return result;
    }

    public IReadOnlyList<WorkItem> ApplyFilters(
        IReadOnlyList<WorkItem> items,
        string? currentUserCanonicalKey,
        string? currentIterationPath,
        bool mineOnly,
        bool currentSprintOnly,
        bool bugsOnly,
        bool unassignedOnly,
        string? textFilter)
    {
        IEnumerable<WorkItem> query = items;

        if (mineOnly && !string.IsNullOrEmpty(currentUserCanonicalKey))
            query = query.Where(i => i.AssignedToCanonicalKey.Equals(currentUserCanonicalKey, StringComparison.OrdinalIgnoreCase));

        if (currentSprintOnly && !string.IsNullOrEmpty(currentIterationPath))
            query = query.Where(i => i.IterationPath.Equals(currentIterationPath, StringComparison.OrdinalIgnoreCase));

        if (bugsOnly)
            query = query.Where(i => i.Type == WorkItemType.Bug);

        if (unassignedOnly)
            query = query.Where(i => string.IsNullOrEmpty(i.AssignedToCanonicalKey));

        if (!string.IsNullOrWhiteSpace(textFilter))
            query = query.Where(i =>
                i.Title.Contains(textFilter, StringComparison.OrdinalIgnoreCase) ||
                i.Id.ToString().Contains(textFilter));

        return query.ToList();
    }
}
