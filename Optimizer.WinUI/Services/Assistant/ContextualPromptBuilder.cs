using System.Text;
using Optimizer.WinUI.Services.Analytics;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>
/// Assembles a dynamic system prompt: a stable base (which is what prompt-caching keys on
/// when context is unchanged) followed by a "Learned context" section reflecting the user's
/// current situation and what has worked there before.
/// </summary>
public class ContextualPromptBuilder(
    IContextDetectionService contextDetection,
    IActionAnalyticsService analytics,
    IPatternExtractionService patterns,
    IAssistantFeedbackService feedback) : IContextualPromptBuilder
{
    public const string BasePrompt =
        "You are the assistant inside Optimizer, a Windows PC optimization app. " +
        "Use the provided tools to answer questions about the user's PC and to perform actions they request. " +
        "Read-only tools run immediately. Tools that change the system require user confirmation, which the app handles — " +
        "call them normally and the app will prompt the user. Be concise. If a tool returns an error, explain it plainly. " +
        "Prefer calling list_profiles before apply_profile so you use a real id. " +
        "When the user asks about startup items/programs, call get_startup_items first and base your " +
        "answer on the actual entries — do not give generic advice. " +
        "You are reactive: act on what the user asks; suggest improvements only when they ask for suggestions.";

    public async Task<string> BuildAsync()
    {
        var sb = new StringBuilder(BasePrompt);

        string context;
        try { context = await contextDetection.DetectContextAsync(); }
        catch { context = "Unknown"; }

        sb.Append("\n\n## Learned context\n");
        sb.Append($"The user currently appears to be in **{context}** context.\n");

        // Declared setup intent from Windows (HKCU CloudExperienceHost\Intent bitmask).
        // Useful as a stable hint about what the user wants this PC to be for.
        try
        {
            var intent = contextDetection.UserIntent;
            if (intent != UserIntent.None)
                sb.Append($"At setup the user declared this PC is for: {intent.ToPromptHint()}.\n");
        }
        catch (Exception ex) { EngineLog.Error("Prompt: user-intent section failed (skipped)", ex); }

        // Most reliable tools in this context.
        try
        {
            var reliable = await analytics.GetMostReliableToolsAsync(context, 3);
            if (reliable.Count > 0)
            {
                sb.Append("Tools that have worked reliably here: ");
                sb.Append(string.Join(", ",
                    reliable.Select(r => $"{r.ToolId} ({r.SuccessRate * 100:F0}% over {r.TotalInvocations})")));
                sb.Append(".\n");
            }
        }
        catch (Exception ex) { EngineLog.Error("Prompt: reliable-tools section failed (skipped)", ex); }

        // Best learned action sequence for this context.
        try
        {
            var pattern = await patterns.GetBestPatternAsync(context);
            if (pattern != null)
                sb.Append($"A sequence that often succeeds here: {string.Join(" → ", pattern.ActionSequence)}.\n");
        }
        catch (Exception ex) { EngineLog.Error("Prompt: learned-pattern section failed (skipped)", ex); }

        // Tools the user has explicitly liked recently.
        try
        {
            var recent = await feedback.GetRecentFeedbackAsync(50);
            var liked = recent
                .Where(f => f.Verdict == FeedbackVerdict.Liked)
                .Select(f => f.ToolId)
                .Distinct()
                .Take(5)
                .ToList();
            if (liked.Count > 0)
                sb.Append($"The user has given positive feedback on: {string.Join(", ", liked)}.\n");
        }
        catch (Exception ex) { EngineLog.Error("Prompt: liked-tools section failed (skipped)", ex); }

        sb.Append("Use these as hints only — always honor the user's explicit request and confirm system changes.");
        return sb.ToString();
    }
}
