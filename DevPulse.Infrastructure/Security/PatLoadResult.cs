namespace DevPulse.Infrastructure.Security;

public enum PatLoadStatus { Ok, Missing, Unreadable }

public sealed record PatLoadResult(PatLoadStatus Status, string? Value)
{
    public bool IsOk => Status == PatLoadStatus.Ok;
    public static readonly PatLoadResult Missing = new(PatLoadStatus.Missing, null);
    public static readonly PatLoadResult Unreadable = new(PatLoadStatus.Unreadable, null);
    public static PatLoadResult Ok(string pat) => new(PatLoadStatus.Ok, pat);
}
