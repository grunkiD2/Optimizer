using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class UndoLastCommand(IWindowsOptimizerService optimizer) : IAppCommand
{
    public string Id => "undo_last";
    public string Description => "Undo the most recent reversible change made by the app.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var entries = optimizer.GetUndoEntries();
        if (entries.Count == 0) return CommandResult.Ok("Nothing to undo.");
        var last = entries[0];
        var ok = await optimizer.UndoEntryAsync(last);
        return ok
            ? CommandResult.Ok($"Reverted: {last.Description}")
            : CommandResult.Fail($"Could not revert: {last.Description}");
    }
}
