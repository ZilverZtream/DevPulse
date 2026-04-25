using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevPulse.Core.Services;

public static class SharedJsonOptions
{
    // Used by ADO response deserialization (PR DTOs, work item DTOs).
    public static readonly JsonSerializerOptions AdoResponse = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Used by settings persistence — indented for human-edit friendliness.
    public static readonly JsonSerializerOptions Settings = new()
    {
        WriteIndented = true
    };
}
