using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevPulse.Core.Interfaces;

namespace DevPulse.Infrastructure.AzureDevOps;

public sealed class WorkItemClient : IWorkItemClient
{
    private readonly HttpClient _http;
    private readonly string _orgUrl;
    private readonly string _project;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string Fields = "System.Id,System.Title,System.WorkItemType,System.State,Microsoft.VSTS.Common.Priority," +
                                  "System.AssignedTo,System.AreaPath,System.IterationPath,System.StateChangeDate,System.TeamProject";

    public WorkItemClient(HttpClient http, string orgUrl, string project)
    {
        _http = http;
        _orgUrl = orgUrl.TrimEnd('/');
        _project = project;
    }

    public async Task<IReadOnlyList<WorkItemDto>> GetWorkItemsAsync(string areaPath, string? iterationPath, CancellationToken ct = default)
    {
        var ids = await GetIdsViaWiqlAsync(areaPath, iterationPath, ct);
        if (ids.Count == 0) return [];
        return await FetchBatchAsync(ids, ct);
    }

    private async Task<List<int>> GetIdsViaWiqlAsync(string areaPath, string? iterationPath, CancellationToken ct)
    {
        var wiql = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{_project}' " +
                   $"AND [System.AreaPath] UNDER '{areaPath}' " +
                   (string.IsNullOrEmpty(iterationPath) ? "" : $"AND [System.IterationPath] UNDER '{iterationPath}' ") +
                   "AND [System.State] <> 'Removed' ORDER BY [System.ChangedDate] DESC";

        var url = $"{_orgUrl}/{Uri.EscapeDataString(_project)}/_apis/wit/wiql?api-version={ApiVersions.WorkItemQueryLanguage}";
        var content = new StringContent(JsonSerializer.Serialize(new { query = wiql }), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<WiqlResult>(body, JsonOpts);
        return result?.WorkItems?.Select(w => w.Id).ToList() ?? [];
    }

    private async Task<List<WorkItemDto>> FetchBatchAsync(List<int> ids, CancellationToken ct)
    {
        var items = new List<WorkItemDto>();
        foreach (var chunk in ids.Chunk(200))
        {
            var idList = string.Join(",", chunk);
            var url = $"{_orgUrl}/_apis/wit/workitems?ids={idList}&fields={Fields}&$expand=relations&api-version={ApiVersions.WorkItemsBatch}";
            var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<AdoListResponse<AdoWorkItem>>(body, JsonOpts);
            if (result?.Value != null)
                items.AddRange(result.Value.Select(Map));
        }
        return items;
    }

    private static WorkItemDto Map(AdoWorkItem w)
    {
        var f = w.Fields ?? new();
        return new WorkItemDto
        {
            Id = w.Id,
            Title = f.GetValueOrDefault("System.Title")?.ToString() ?? string.Empty,
            WorkItemType = f.GetValueOrDefault("System.WorkItemType")?.ToString() ?? string.Empty,
            State = f.GetValueOrDefault("System.State")?.ToString() ?? string.Empty,
            Priority = f.GetValueOrDefault("Microsoft.VSTS.Common.Priority")?.ToString() ?? string.Empty,
            AssignedToDisplayName = ExtractDisplayName(f.GetValueOrDefault("System.AssignedTo")),
            AssignedToUniqueName = ExtractUniqueName(f.GetValueOrDefault("System.AssignedTo")),
            AreaPath = f.GetValueOrDefault("System.AreaPath")?.ToString() ?? string.Empty,
            IterationPath = f.GetValueOrDefault("System.IterationPath")?.ToString() ?? string.Empty,
            Url = w.Url ?? string.Empty,
            StateChangedDate = ParseDate(f.GetValueOrDefault("System.StateChangeDate")),
            Relations = w.Relations?.Select(r => new WorkItemRelationDto { Rel = r.Rel ?? string.Empty, Url = r.Url ?? string.Empty }).ToList() ?? []
        };
    }

    private static string ExtractDisplayName(object? val)
    {
        if (val is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("displayName", out var dn)) return dn.GetString() ?? string.Empty;
        }
        return val?.ToString() ?? string.Empty;
    }

    private static string ExtractUniqueName(object? val)
    {
        if (val is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("uniqueName", out var un)) return un.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static DateTimeOffset ParseDate(object? val)
        => val is JsonElement je && DateTimeOffset.TryParse(je.GetString(), out var dt) ? dt : DateTimeOffset.UtcNow;

    private sealed class AdoListResponse<T> { public List<T>? Value { get; set; } }
    private sealed class WiqlResult { public List<WiqlItem>? WorkItems { get; set; } }
    private sealed class WiqlItem { public int Id { get; set; } }
    private sealed class AdoWorkItem
    {
        public int Id { get; set; }
        public string? Url { get; set; }
        public Dictionary<string, object>? Fields { get; set; }
        public List<AdoRelation>? Relations { get; set; }
    }
    private sealed class AdoRelation { public string? Rel { get; set; } public string? Url { get; set; } }
}
