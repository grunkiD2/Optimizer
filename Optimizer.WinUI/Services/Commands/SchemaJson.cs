using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

/// <summary>Small helpers for building tool input schemas as JsonElement.</summary>
public static class SchemaJson
{
    /// <summary>A no-parameter object schema.</summary>
    public static JsonElement Empty { get; } =
        JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

    /// <summary>Parse a JSON-schema string into a JsonElement (kept alive for the process).</summary>
    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
