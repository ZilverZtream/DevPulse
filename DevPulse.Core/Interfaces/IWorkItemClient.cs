namespace DevPulse.Core.Interfaces;

public interface IWorkItemClient
{
    Task<IReadOnlyList<WorkItemDto>> GetWorkItemsAsync(string areaPath, string? iterationPath, CancellationToken ct = default);
}

public sealed class WorkItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string WorkItemType { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string AssignedToDisplayName { get; set; } = string.Empty;
    public string AssignedToUniqueName { get; set; } = string.Empty;
    public string AreaPath { get; set; } = string.Empty;
    public string IterationPath { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset StateChangedDate { get; set; }
    public List<WorkItemRelationDto> Relations { get; set; } = [];
}

public sealed class WorkItemRelationDto
{
    public string Rel { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
