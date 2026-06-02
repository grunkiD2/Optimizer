namespace Optimizer.WinUI.Services;

public class DnsServerPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Primary { get; set; } = "";
    public string Secondary { get; set; } = "";
}

public interface INetworkConfigService
{
    IReadOnlyList<DnsServerPreset> DnsPresets { get; }
    Task<string> GetCurrentPrimaryDnsAsync();
    Task<bool> SetDnsAsync(string primary, string secondary);
    Task<bool> ResetDnsToAutomaticAsync();
    Task<bool> FlushDnsAsync();
}
