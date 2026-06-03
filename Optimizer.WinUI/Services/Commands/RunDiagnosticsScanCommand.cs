using System.Text;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class RunDiagnosticsScanCommand(IDiagnosticsService diagnostics) : IAppCommand
{
    public string Id => "run_diagnostics_scan";
    public string Description => "Run a full diagnostics scan and summarize the findings.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var findings = await diagnostics.RunFullScanAsync();
        if (findings.Count == 0) return CommandResult.Ok("Diagnostics scan complete — no issues found.");
        var sb = new StringBuilder($"Diagnostics found {findings.Count} item(s):");
        foreach (var f in findings)
            sb.Append($"\n- [{f.Severity}] {f.Title}: {f.Recommendation}");
        return CommandResult.Ok(sb.ToString());
    }
}
