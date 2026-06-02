namespace Optimizer.WinUI.Services;

public class NetworkConfigService : INetworkConfigService
{
    private readonly IPowerShellRunner _psRunner;

    public NetworkConfigService(IPowerShellRunner psRunner)
    {
        _psRunner = psRunner;
    }

    public IReadOnlyList<DnsServerPreset> DnsPresets { get; } = new[]
    {
        new DnsServerPreset { Id = "cloudflare",        Name = "Cloudflare",        Description = "Fast, privacy-focused",                          Primary = "1.1.1.1",         Secondary = "1.0.0.1" },
        new DnsServerPreset { Id = "cloudflare-family", Name = "Cloudflare Family", Description = "Blocks malware and adult content",               Primary = "1.1.1.3",         Secondary = "1.0.0.3" },
        new DnsServerPreset { Id = "google",            Name = "Google",            Description = "Reliable, widely used",                          Primary = "8.8.8.8",         Secondary = "8.8.4.4" },
        new DnsServerPreset { Id = "quad9",             Name = "Quad9",             Description = "Security-focused, blocks malicious domains",     Primary = "9.9.9.9",         Secondary = "149.112.112.112" },
        new DnsServerPreset { Id = "opendns",           Name = "OpenDNS",           Description = "Cisco-backed, configurable filtering",           Primary = "208.67.222.222",  Secondary = "208.67.220.220" },
        new DnsServerPreset { Id = "adguard",           Name = "AdGuard DNS",       Description = "Blocks ads and trackers",                        Primary = "94.140.14.14",    Secondary = "94.140.15.15" },
    };

    public async Task<string> GetCurrentPrimaryDnsAsync()
    {
        var output = await _psRunner.RunAsync(
            "(Get-DnsClientServerAddress -AddressFamily IPv4 | Where-Object {$_.ServerAddresses.Count -gt 0} | Select-Object -First 1).ServerAddresses[0]");
        return output?.Trim() ?? "";
    }

    public async Task<bool> SetDnsAsync(string primary, string secondary)
    {
        var script = $@"Get-NetAdapter | Where-Object {{$_.Status -eq 'Up'}} | ForEach-Object {{ Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses ('{primary}','{secondary}') }}";
        return await _psRunner.RunAsync(script) != null;
    }

    public async Task<bool> ResetDnsToAutomaticAsync()
    {
        var script = @"Get-NetAdapter | Where-Object {$_.Status -eq 'Up'} | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses }";
        return await _psRunner.RunAsync(script) != null;
    }

    public async Task<bool> FlushDnsAsync()
    {
        return await _psRunner.RunAsync("Clear-DnsClientCache") != null;
    }
}
