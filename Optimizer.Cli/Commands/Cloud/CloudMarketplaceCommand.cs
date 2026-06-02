using System.CommandLine;

namespace Optimizer.Cli.Commands.Cloud;

public class CloudMarketplaceCommand : Command
{
    public CloudMarketplaceCommand() : base("marketplace", "Browse the Optimizer marketplace")
    {
        var categoryOption = new Option<string?>("--category", "Filter by category");
        var searchOption   = new Option<string?>("--search",   "Search term");
        var pageOption     = new Option<int>("--page", () => 1, "Page number");
        AddOption(categoryOption);
        AddOption(searchOption);
        AddOption(pageOption);

        this.SetHandler(async (string? category, string? search, int page) =>
        {
            var api = CloudApiClient.FromEnv();

            var qs = BuildQueryString(category, search, page);
            var result = await api.GetAsync($"/api/marketplace{qs}");
            if (result == null) return;

            var root    = result.RootElement;
            var total   = root.TryGetProperty("total",    out var t)  ? t.GetInt32()  : 0;
            var pg      = root.TryGetProperty("page",     out var p)  ? p.GetInt32()  : page;
            var pgSize  = root.TryGetProperty("pageSize", out var ps) ? ps.GetInt32() : 20;

            Console.WriteLine($"Marketplace Listings  (page {pg}, {total} total)");
            Console.WriteLine("────────────────────────────────────────────────────────────────────────");
            Console.WriteLine($"{"Name",-30} {"Author",-20} {"Category",-15} {"↓",-8} {"★",-6} {"V",-4}");
            Console.WriteLine($"{"────",-30} {"──────",-20} {"────────",-15} {"─",-8} {"─",-6} {"─",-4}");

            if (!root.TryGetProperty("listings", out var listings))
            {
                Console.WriteLine("No listings found.");
                return;
            }

            foreach (var item in listings.EnumerateArray())
            {
                var name     = TryStr(item, "name",                "—");
                var author   = TryStr(item, "authorDisplayName",   "—");
                var cat      = TryStr(item, "category",            "—");
                var downloads = item.TryGetProperty("downloads",     out var d)  ? d.GetInt32()          : 0;
                var rating    = item.TryGetProperty("averageRating", out var r)  ? r.GetDouble()         : 0;
                var verified  = item.TryGetProperty("verified",      out var vf) ? (vf.GetBoolean() ? "✓" : "") : "";

                Console.WriteLine($"{Truncate(name,30),-30} {Truncate(author,20),-20} {Truncate(cat,15),-15} {downloads,-8} {rating:F1,-6} {verified,-4}");
            }

            Console.WriteLine();
            Console.WriteLine($"Showing {pgSize} per page. Use --page to navigate.");
        }, categoryOption, searchOption, pageOption);
    }

    private static string BuildQueryString(string? category, string? search, int page)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(category)) parts.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrEmpty(search))   parts.Add($"search={Uri.EscapeDataString(search)}");
        if (page > 1)                         parts.Add($"page={page}");
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    private static string TryStr(System.Text.Json.JsonElement el, string prop, string fallback)
        => el.TryGetProperty(prop, out var v) ? (v.GetString() ?? fallback) : fallback;

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}
