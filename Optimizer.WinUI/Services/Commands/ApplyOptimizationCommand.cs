using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class ApplyOptimizationCommand(IWindowsOptimizerService optimizer) : IAppCommand
{
    public string Id => "apply_optimization";
    public string Description => "Apply a single named optimization by its id. Changes are reversible.";
    public JsonElement ParametersSchema { get; } = SchemaJson.Parse("""
        {"type":"object",
         "properties":{"optimization_id":{"type":"string","description":"The optimization id."}},
         "required":["optimization_id"]}
        """);
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var id = args.TryGetProperty("optimization_id", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(id)) return CommandResult.Fail("No optimization_id supplied.");
        var result = await optimizer.ApplyOptimizationAsync(id);
        return result.Success
            ? CommandResult.Ok(string.IsNullOrWhiteSpace(result.Message) ? $"Applied '{id}'." : result.Message)
            : CommandResult.Fail(string.IsNullOrWhiteSpace(result.Message) ? $"Failed to apply '{id}'." : result.Message);
    }
}
