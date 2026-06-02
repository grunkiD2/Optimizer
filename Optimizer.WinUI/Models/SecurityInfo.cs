namespace Optimizer.WinUI.Models;

public class DefenderStatus
{
    public bool RealTimeProtectionEnabled { get; set; }
    public bool CloudProtectionEnabled { get; set; }
    public bool TamperProtectionEnabled { get; set; }
    public DateTime LastQuickScan { get; set; }
    public DateTime LastFullScan { get; set; }
    public string DefinitionVersion { get; set; } = "";
}

public class FirewallStatus
{
    public bool DomainEnabled { get; set; }
    public bool PrivateEnabled { get; set; }
    public bool PublicEnabled { get; set; }
}

public class BitLockerVolume
{
    public string DriveLetter { get; set; } = "";
    public string ProtectionStatus { get; set; } = "";
    public string EncryptionMethod { get; set; } = "";
    public string LockStatus { get; set; } = "";
}
