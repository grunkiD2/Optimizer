using System.CommandLine;

namespace Optimizer.Cli.Commands.Cloud;

public class CloudPluginsCommand : Command
{
    public CloudPluginsCommand() : base("plugins", "Browse available plugins")
    {
        var searchOption   = new Option<string?>("--search",   "Search term");
        var categoryOption = new Option<string?>("--category", "Filter by category");
        var pageOption     = new Option<int>("--page", () => 1, "Page number");
        AddOption(searchOption);
        AddOption(categoryOption);
        AddOption(pageOption);

        this.SetHandler(async (string? search, string? category, int page) =>
        {
            var api = CloudApiClient.FromEnv();

            var qs = BuildQueryString(search, category, page);
            var result = await api.GetAsync($"/api/plugins{qs}");
            if (result == null) return;

            var root   = result.RootElement;
            var total  = root.TryGetProperty("total",    out var t)  ? t.GetInt32() : 0;
            var pg     = root.TryGetProperty("page",     out var p)  ? p.GetInt32() : page;
            var pgSize = root.TryGetProperty("pageSize", out var ps) ? ps.GetInt32() : 20;

            Console.WriteLine($"Plugin Listings  (page {pg}, {total} total)");
            Console.WriteLine("──────────────────────────────────────────────────────────────────────");
            Console.WriteLine($"{"Plugin ID",-28} {"Name",-24} {"Category",-14} {"↓",-8} {"★",-6} {"V",-4}");
            Console.WriteLine($"{"─────────",-28} {"────",-24} {"────────",-14} {"─",-8} {"─",-6} {"─",-4}");

            if (!root.TryGetProperty("listings", out var listings))
            {
                Console.WriteLine("No plugins found.");
                return;
            }

            foreach (var item in listings.EnumerateArray())
            {
                var pluginId  = TryStr(item, "pluginId",          "—");
                var name      = TryStr(item, "name",              "—");
                var cat       = TryStr(item, "category",          "—");
                var downloads = item.TryGetProperty("downloads",     out var d)  ? d.GetInt32()  : 0;
                var rating    = item.TryGetProperty("averageRating", out var r)  ? r.GetDouble() : 0;
                var verified  = item.TryGetProperty("verified",      out var vf) ? (vf.GetBoolean() ? "✓" : "") : "";

                Console.WriteLine($"{Truncate(pluginId,28),-28} {Truncate(name,24),-24} {Truncate(cat,14),-14} {downloads,-8} {rating:F1,-6} {verified,-4}");
            }

            Console.WriteLine();
            Console.WriteLine($"Showing {pgSize} per page. Use --page to navigate.");
        }, searchOption, categoryOption, pageOption);
    }

    private static string BuildQueryString(string? search, string? category, int page)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(search))   parts.Add($"search={Uri.EscapeDataString(search)}");
        if (!string.IsNullOrEmpty(category)) parts.Add($"category={Uri.EscapeDataString(category)}");
        if (page > 1)                         parts.Add($"page={page}");
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    private static string TryStr(System.Text.Json.JsonElement el, string prop, string fallback)
        => el.TryGetProperty(prop, out var v) ? (v.GetString() ?? fallback) : fallback;

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}
