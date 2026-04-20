namespace DevPulse.Core.Models;

public sealed class BoardColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public List<string> MappedStates { get; set; } = [];
    public int AgingDaysWarning { get; set; } = 2;
    public int AgingDaysStale { get; set; } = 6;
}
