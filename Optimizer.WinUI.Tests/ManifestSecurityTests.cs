using System;
using System.Collections.Generic;
using Moq;
using Optimizer.WinUI.Models.Plugins;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Plugins;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Negative security tests: proves that the exploits identified in the B+C audit
/// are closed after the security fixes are applied.
///
/// Covers:
///   • File path traversal via %TEMP%\..\..\..
///   • Registry path traversal via HKCU\Software\Foo\..\..
///   • schtasks argument injection via '"' in task_name
///   • powercfg injection via shell metacharacters
///   • powercfg allowlist (valid /setactive {GUID} passes)
/// </summary>
[Collection("RegistryTests")]
public class ManifestSecurityTests
{
    private readonly ManifestParser _parser = new();
    private readonly DeclarativeChangeExecutor _executor =
        new(new Mock<IUndoService>().Object);

    // ── Fix 5: File path traversal ───────────────────────────────────────────

    [Fact]
    public void FilePathTraversal_TempDotDotSystem32_Rejected()
    {
        // EXPLOIT: %TEMP%\..\..\..\Windows\System32\drivers\etc\hosts
        // Before fix: ExpandEnvironmentVariables expanded %TEMP%, then StartsWith matched
        // the TEMP root even though the path resolves outside it.
        // After fix: Path.GetFullPath canonicalises the path; the resolved path is under
        // C:\Windows\System32, which is NOT in the allow-list → returns false.
        var traversalPath = @"%TEMP%\..\..\..\Windows\System32\drivers\etc\hosts";
        Assert.False(ManifestPermissions.IsFilePathAllowed(traversalPath),
            "Path traversal via %TEMP%\\..\\..\\.. must be rejected.");
    }

    [Fact]
    public void FilePathTraversal_TempDoubleDotToSystem32_Rejected()
    {
        // Craft a path that traverses from TEMP all the way to C:\Windows\System32.
        // TEMP is typically something like C:\Users\user\AppData\Local\Temp (4 levels deep from C:\).
        // We use enough ".." segments to guarantee we reach C:\, then descend to System32.
        // This tests that GetFullPath + StartsWith catches multi-hop traversal.
        var traversal = @"%TEMP%\..\..\..\..\Windows\System32\evil.dll";
        Assert.False(ManifestPermissions.IsFilePathAllowed(traversal),
            "Path with multiple '..' that reaches C:\\Windows\\System32 must be rejected.");
    }

    [Fact]
    public void FilePathTraversal_ValidTempSubPath_Accepted()
    {
        // Confirm a genuinely valid temp path still passes after the fix
        var validPath = @"%TEMP%\optimizer-cache\somefile.tmp";
        Assert.True(ManifestPermissions.IsFilePathAllowed(validPath),
            "A valid path under %TEMP% must still be accepted.");
    }

    // ── Fix 6: Registry path traversal ───────────────────────────────────────

    [Fact]
    public void RegistryTraversal_DotDotSegment_RejectedByPermissions()
    {
        // EXPLOIT: HKCU\Software\Foo\..\..\..\SYSTEM\Bar
        // The allow-list prefix HKCU\Software\ matched, but CreateSubKey resolves '..'
        // and the write lands outside the allowed subtree.
        // After fix: IsRegistryPathAllowed rejects any path with '.' or '..' segments.
        var traversalPath = @"HKCU\Software\Foo\..\..\..\SYSTEM\Bar";
        Assert.False(ManifestPermissions.IsRegistryPathAllowed(traversalPath),
            "Registry path with '..' traversal segments must be rejected.");
    }

    [Fact]
    public void RegistryTraversal_DotDotSegment_ValidatePermissionsReturnsViolation()
    {
        // End-to-end: a manifest containing the traversal path must produce a violation
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-registry-traversal",
            Name = "Registry Traversal Test",
            Category = "System",
            Changes =
            {
                new ManifestChange
                {
                    Type    = "registry",
                    Path    = @"HKCU\Software\Foo\..\..\..\SYSTEM\Bar",
                    Value   = "Evil",
                    Apply   = "1"
                }
            }
        };

        var ok = _executor.ValidatePermissions(manifest, out var violations);

        Assert.False(ok, "ValidatePermissions must reject a manifest with a registry traversal path.");
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void RegistryTraversal_ValidPath_Accepted()
    {
        // Confirm a normal allowed registry path still passes
        Assert.True(ManifestPermissions.IsRegistryPathAllowed(@"HKCU\Software\MyApp\Settings"),
            "A valid HKCU\\Software path must still be accepted.");
    }

    // ── Fix 3: schtasks task name injection ───────────────────────────────────

    [Fact]
    public void TaskNameInjection_QuoteCharacter_RejectedByValidate()
    {
        // EXPLOIT: task_name = "Legit\" /Create /TR calc.exe /SC ONLOGON"
        // The '"' breaks out of the schtasks /TN "..." quoting and injects extra flags.
        // After fix: ManifestParser.Validate rejects task names containing '"'.
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-schtasks-injection",
            Name = "schtasks injection test",
            Category = "System",
            Changes =
            {
                new ManifestChange
                {
                    Type       = "scheduled-task",
                    TaskName   = "Legit\" /Create /TR calc.exe /SC ONLOGON",
                    TaskAction = "disable"
                }
            }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("disallowed characters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TaskNameInjection_ControlCharacter_RejectedByValidate()
    {
        // Task name containing a newline
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-schtasks-ctrl",
            Name = "schtasks ctrl char test",
            Category = "System",
            Changes =
            {
                new ManifestChange
                {
                    Type       = "scheduled-task",
                    TaskName   = "Legit\nTaskName",
                    TaskAction = "disable"
                }
            }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("disallowed characters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TaskName_ValidPath_AcceptedByValidate()
    {
        // Task names with backslash paths (e.g. Microsoft\Windows\Defrag\ScheduledDefrag) must pass
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-schtasks-valid",
            Name = "schtasks valid name",
            Category = "System",
            Changes =
            {
                new ManifestChange
                {
                    Type       = "scheduled-task",
                    TaskName   = @"Microsoft\Windows\Defrag\ScheduledDefrag",
                    TaskAction = "disable"
                }
            }
        };

        var errors = _parser.Validate(manifest);
        Assert.DoesNotContain(errors, e => e.Contains("disallowed characters", StringComparison.OrdinalIgnoreCase));
    }

    // ── Fix 4: powercfg injection ─────────────────────────────────────────────

    [Fact]
    public void PowerCfgInjection_ShellAmpersand_RejectedByValidate()
    {
        // EXPLOIT: power_cfg_args = "/h off & calc.exe"
        // Before fix: PowerCfgArgs was passed verbatim; '&' ran calc.exe as a separate command.
        // After fix: Validate rejects any PowerCfgArgs containing '&'.
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-powercfg-injection",
            Name = "powercfg injection test",
            Category = "System",
            Changes =
            {
                new ManifestChange
                {
                    Type          = "powercfg",
                    PowerCfgArgs  = "/h off & calc.exe"
                }
            }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("forbidden characters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PowerCfgInjection_Pipe_RejectedByValidate()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-powercfg-pipe",
            Name = "powercfg pipe injection",
            Category = "System",
            Changes =
            {
                new ManifestChange
                {
                    Type         = "powercfg",
                    PowerCfgArgs = "/h off | net user hacker /add"
                }
            }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("forbidden characters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PowerCfgInjection_UnknownSubCommand_RejectedByValidate()
    {
        // Only allowlisted sub-commands are permitted; unknown ones must be rejected
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-powercfg-unknown",
            Name = "powercfg unknown subcmd",
            Category = "System",
            Changes =
            {
                new ManifestChange
                {
                    Type         = "powercfg",
                    PowerCfgArgs = "/import evil-scheme.pow"
                }
            }
        };

        var errors = _parser.Validate(manifest);
        Assert.Contains(errors, e => e.Contains("not permitted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PowerCfgAllowlist_SetActiveGuid_Accepted()
    {
        // A valid /setactive with a proper GUID must pass validation
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-powercfg-setactive",
            Name = "powercfg setactive valid",
            Category = "Performance",
            Changes =
            {
                new ManifestChange
                {
                    Type         = "powercfg",
                    PowerCfgArgs = "/setactive {8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c}"
                }
            }
        };

        var errors = _parser.Validate(manifest);
        Assert.DoesNotContain(errors, e => e.Contains("powercfg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PowerCfgAllowlist_HibernateOff_Accepted()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-powercfg-hibernate",
            Name = "powercfg hibernate off",
            Category = "Performance",
            Changes =
            {
                new ManifestChange
                {
                    Type         = "powercfg",
                    PowerCfgArgs = "/h off"
                }
            }
        };

        var errors = _parser.Validate(manifest);
        Assert.DoesNotContain(errors, e => e.Contains("powercfg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PowerCfgAllowlist_ChangeTimeout_Accepted()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-powercfg-change",
            Name = "powercfg change timeout",
            Category = "Performance",
            Changes =
            {
                new ManifestChange
                {
                    Type         = "powercfg",
                    PowerCfgArgs = "/change sleep-timeout-ac 0"
                }
            }
        };

        var errors = _parser.Validate(manifest);
        Assert.DoesNotContain(errors, e => e.Contains("powercfg", StringComparison.OrdinalIgnoreCase));
    }
}
