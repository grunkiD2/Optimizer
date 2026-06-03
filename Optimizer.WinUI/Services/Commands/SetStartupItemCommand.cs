using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

/// <summary>Enables or disables a single startup item by name. Reversible; requires confirmation.</summary>
public sealed class SetStartupItemCommand(IStartupService startup) : IAppCommand
{
    public string Id => "set_startup_item";
    public string Description =>
        "Enable or disable a startup item by its exact name (as returned by get_startup_items). " +
        "Disabling stops it launching at sign-in; this is reversible.";
    public JsonElement ParametersSchema { get; } = SchemaJson.Parse("""
        {"type":"object",
         "properties":{
            "name":{"type":"string","description":"Exact startup item name from get_startup_items."},
            "enabled":{"type":"boolean","description":"true to enable at startup, false to disable."}},
         "required":["name","enabled"]}
        """);
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var name = args.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(CommandResult.Fail("No startup item name supplied."));
        if (!args.TryGetProperty("enabled", out var e) || e.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Task.FromResult(CommandResult.Fail("Missing 'enabled' boolean."));

        var enabled = e.GetBoolean();
        var entry = startup.GetEntries()
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return Task.FromResult(CommandResult.Fail($"No startup item named '{name}'."));

        if (entry.RequiresAdmin && !IsElevated())
            return Task.FromResult(CommandResult.Fail(
                $"'{name}' is a machine-wide item ({entry.LocationText}) and needs administrator rights to change."));

        var ok = startup.SetEnabled(entry, enabled);
        return Task.FromResult(ok
            ? CommandResult.Ok($"{(enabled ? "Enabled" : "Disabled")} startup item '{name}'.")
            : CommandResult.Fail($"Failed to change startup item '{name}'."));
    }

    private static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
