using System.Text;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class ListProfilesCommand(IWindowsOptimizerService optimizer) : IAppCommand
{
    public string Id => "list_profiles";
    public string Description => "List the built-in optimization profiles/presets the user can apply (with their ids).";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var presets = optimizer.GetBuiltInPresets();
        var sb = new StringBuilder($"{presets.Count} profile(s):");
        foreach (var p in presets)
            sb.Append($"\n- {p.Name} (id: {p.Id}) — {p.Description}");
        return Task.FromResult(CommandResult.Ok(sb.ToString(),
            presets.Select(p => new { p.Id, p.Name, p.Description })));
    }
}
