namespace DevPulse.Core.Services;

public static class WiqlPathGuard
{
    public static string ValidatePath(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Path '{paramName}' cannot be empty.", paramName);
        if (value.Any(c => c < 32 || c == ';' || c == '\''))
            throw new ArgumentException($"Path '{paramName}' contains invalid characters.", paramName);
        return value;
    }
}
