using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ComplianceServiceTests
{
    [Fact]
    public void AvailableFrameworks_ContainsExpectedSet()
    {
        var privacy = new Mock<IPrivacyService>();
        var security = new Mock<ISecurityService>();
        var service = new ComplianceService(privacy.Object, security.Object);

        Assert.Contains("CIS Benchmark", service.AvailableFrameworks);
        Assert.Contains("NIST 800-171", service.AvailableFrameworks);
        Assert.Contains("HIPAA", service.AvailableFrameworks);
        Assert.Contains("SOC 2", service.AvailableFrameworks);
    }

    [Fact]
    public async Task RunFrameworkAsync_CisBenchmark_ReturnsChecks()
    {
        var privacy = new Mock<IPrivacyService>();
        privacy.Setup(p => p.GetAllAsync())
               .ReturnsAsync(new List<PrivacySetting>());

        var security = new Mock<ISecurityService>();
        security.Setup(s => s.GetDefenderStatusAsync())
                .ReturnsAsync(new DefenderStatus { RealTimeProtectionEnabled = true });
        security.Setup(s => s.GetFirewallStatusAsync())
                .ReturnsAsync(new FirewallStatus { DomainEnabled = true, PrivateEnabled = true, PublicEnabled = true });
        security.Setup(s => s.GetBitLockerStatusAsync())
                .ReturnsAsync(new List<BitLockerVolume>());

        var service = new ComplianceService(privacy.Object, security.Object);
        var checks = await service.RunFrameworkAsync("CIS Benchmark");

        Assert.NotEmpty(checks);
        Assert.All(checks, c => Assert.Equal("CIS Benchmark", c.Framework));
    }

    [Fact]
    public async Task RunFrameworkAsync_AllFrameworks_ReturnChecksForEachFramework()
    {
        var privacy = new Mock<IPrivacyService>();
        privacy.Setup(p => p.GetAllAsync())
               .ReturnsAsync(new List<PrivacySetting>());

        var security = new Mock<ISecurityService>();
        security.Setup(s => s.GetDefenderStatusAsync())
                .ReturnsAsync(new DefenderStatus());
        security.Setup(s => s.GetFirewallStatusAsync())
                .ReturnsAsync(new FirewallStatus());
        security.Setup(s => s.GetBitLockerStatusAsync())
                .ReturnsAsync(new List<BitLockerVolume>());

        var service = new ComplianceService(privacy.Object, security.Object);

        // Each framework should return a non-empty list
        foreach (var framework in service.AvailableFrameworks)
        {
            var checks = await service.RunFrameworkAsync(framework);
            Assert.NotEmpty(checks);
        }
    }

    [Fact]
    public async Task RunFrameworkAsync_PassingDefender_ProducesPassChecks()
    {
        var privacy = new Mock<IPrivacyService>();
        privacy.Setup(p => p.GetAllAsync()).ReturnsAsync(new List<PrivacySetting>());

        var security = new Mock<ISecurityService>();
        security.Setup(s => s.GetDefenderStatusAsync())
                .ReturnsAsync(new DefenderStatus
                {
                    RealTimeProtectionEnabled = true,
                    CloudProtectionEnabled = true,
                    TamperProtectionEnabled = true
                });
        security.Setup(s => s.GetFirewallStatusAsync())
                .ReturnsAsync(new FirewallStatus { DomainEnabled = true, PrivateEnabled = true, PublicEnabled = true });
        security.Setup(s => s.GetBitLockerStatusAsync())
                .ReturnsAsync(new List<BitLockerVolume>());

        var service = new ComplianceService(privacy.Object, security.Object);
        var checks = await service.RunFrameworkAsync("CIS Benchmark");

        // With Defender fully on, the Defender-related checks should pass
        var defenderCheck = checks.FirstOrDefault(c => c.ControlId == "CIS-1.1.1");
        Assert.NotNull(defenderCheck);
        Assert.Equal(ComplianceStatus.Pass, defenderCheck!.Status);
    }
}
