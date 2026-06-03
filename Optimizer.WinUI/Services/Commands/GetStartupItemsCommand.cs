using System.Text;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

/// <summary>Lists the programs that launch at sign-in so the assistant can reason about startup impact.</summary>
public sealed class GetStartupItemsCommand(IStartupService startup) : IAppCommand
{
    public string Id => "get_startup_items";
    public string Description =>
        "List the programs and services that run at Windows sign-in (startup items), " +
        "including whether each is currently enabled and where it is registered. " +
        "Use this before advising on or changing startup items.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var entries = startup.GetEntries();
        if (entries.Count == 0)
            return Task.FromResult(CommandResult.Ok("No startup items found."));

        var sb = new StringBuilder($"{entries.Count} startup item(s):");
        foreach (var e in entries.OrderByDescending(e => e.Enabled).ThenBy(e => e.Name))
            sb.Append($"\n- {e.Name} [{(e.Enabled ? "enabled" : "disabled")}] — {e.LocationText}");

        var data = entries.Select(e => new
        {
            name = e.Name,
            enabled = e.Enabled,
            location = e.LocationText,
            command = e.Command,
            requiresAdmin = e.RequiresAdmin
        });

        return Task.FromResult(CommandResult.Ok(sb.ToString(), data));
    }
}
