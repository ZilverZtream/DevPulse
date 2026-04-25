namespace DevPulse.Core.Services;

public static class WiqlPathGuard
{
    public static string ValidatePath(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Path '{paramName}' cannot be empty.", paramName);
        // Defense-in-depth: block both ';' and '\'' at the guard layer even though WiqlLiteral()
        // escapes single quotes at the query builder. If any future call site forgets to escape,
        // the guard still prevents injection.
        if (value.Any(c => c < 32 || c == ';' || c == '\''))
            throw new ArgumentException($"Path '{paramName}' contains invalid characters.", paramName);
        return value;
    }
}
