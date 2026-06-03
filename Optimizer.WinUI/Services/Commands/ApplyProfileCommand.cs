using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class ApplyProfileCommand(IWindowsOptimizerService optimizer) : IAppCommand
{
    public string Id => "apply_profile";
    public string Description => "Apply a built-in optimization profile by its id (use list_profiles first to get ids). Changes are reversible.";
    public JsonElement ParametersSchema { get; } = SchemaJson.Parse("""
        {"type":"object",
         "properties":{"profile_id":{"type":"string","description":"The profile id, e.g. preset-privacy."}},
         "required":["profile_id"]}
        """);
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var id = args.TryGetProperty("profile_id", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(id)) return CommandResult.Fail("No profile_id supplied.");
        var ok = await optimizer.ApplyProfileAsync(id);
        return ok
            ? CommandResult.Ok($"Applied profile '{id}'. You can undo it from the History page or by asking me to undo.")
            : CommandResult.Fail($"Failed to apply profile '{id}'.");
    }
}
