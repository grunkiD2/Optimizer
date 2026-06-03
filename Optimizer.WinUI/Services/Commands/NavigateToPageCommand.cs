using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class NavigateToPageCommand(IPageNavigator navigator) : IAppCommand
{
    public string Id => "navigate_to_page";
    public string Description => "Open one of the app's pages so the user can see it.";
    public JsonElement ParametersSchema { get; } = SchemaJson.Parse("""
        {"type":"object",
         "properties":{"page":{"type":"string","description":"Page tag to open, e.g. Dashboard, Diagnostics, Updates, Security, Tuning, Profiles."}},
         "required":["page"]}
        """);
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var page = args.TryGetProperty("page", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(page))
            return Task.FromResult(CommandResult.Fail("No page specified."));
        var ok = navigator.NavigateTo(page);
        return Task.FromResult(ok
            ? CommandResult.Ok($"Opened the {page} page.")
            : CommandResult.Fail($"Unknown page '{page}'. Known pages: {string.Join(", ", navigator.Pages)}"));
    }
}
