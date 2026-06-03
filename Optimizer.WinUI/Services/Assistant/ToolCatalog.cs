using Optimizer.WinUI.Services.Commands;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Generates Claude tool definitions from the command registry, honoring the allow-actions toggle.</summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ClaudeToolDef> Build(ICommandRegistry registry, bool allowActions)
    {
        var tools = new List<ClaudeToolDef>();
        foreach (var c in registry.Commands)
        {
            // When the user has disabled actions, drop everything that would change the system.
            if (!allowActions && c.RequiresConfirmation) continue;
            tools.Add(new ClaudeToolDef(c.Id, c.Description, c.ParametersSchema));
        }
        return tools;
    }
}
