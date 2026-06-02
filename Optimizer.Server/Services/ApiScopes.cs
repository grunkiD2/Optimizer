namespace Optimizer.Server.Services;

public static class ApiScopes
{
    public const string MetricsRead    = "metrics:read";
    public const string ProfilesRead   = "profiles:read";
    public const string ProfilesWrite  = "profiles:write";
    public const string SyncRead       = "sync:read";
    public const string SyncWrite      = "sync:write";
    public const string MarketplaceRead = "marketplace:read";
    public const string PluginsRead    = "plugins:read";
    public const string PluginsManage  = "plugins:manage";

    public static readonly IReadOnlyList<string> All = new[]
    {
        MetricsRead, ProfilesRead, ProfilesWrite, SyncRead, SyncWrite,
        MarketplaceRead, PluginsRead, PluginsManage
    };

    public static bool IsValid(string scope) => All.Contains(scope);
}
