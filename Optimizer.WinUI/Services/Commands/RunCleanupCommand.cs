using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Commands;

public sealed class RunCleanupCommand(IWindowsOptimizerService optimizer) : IAppCommand
{
    public string Id => "run_cleanup";
    public string Description => "Clear temporary files to free disk space.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var result = await optimizer.ApplyOptimizationAsync(OptimizationIds.ClearTemporaryFiles);
        return result.Success
            ? CommandResult.Ok(string.IsNullOrWhiteSpace(result.Message) ? "Temporary files cleared." : result.Message)
            : CommandResult.Fail(string.IsNullOrWhiteSpace(result.Message) ? "Cleanup failed." : result.Message);
    }
}
