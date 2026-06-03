using System.Text;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class GetRecommendationsCommand(IRecommendationsService recs) : IAppCommand
{
    public string Id => "get_recommendations";
    public string Description => "List the current optimization and health recommendations for this PC.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var list = await recs.GenerateAsync();
        if (list.Count == 0) return CommandResult.Ok("No recommendations right now — the system looks healthy.");
        var sb = new StringBuilder($"{list.Count} recommendation(s):");
        foreach (var r in list)
            sb.Append($"\n- [{r.Severity}] {r.Title} (id: {r.Id})");
        return CommandResult.Ok(sb.ToString(), list.Select(r => new { r.Id, r.Title, severity = r.Severity.ToString() }));
    }
}
