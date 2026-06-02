using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Win32;
using Moq;
using Optimizer.WinUI.Models.Plugins;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Plugins;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Unit tests for DeclarativeChangeExecutor (B2) and ManifestPermissions.
///
/// Registry tests use a sacrificial key under HKCU\Software\OptimizerPluginTest
/// which is deleted in Dispose.
/// </summary>
public class DeclarativeChangeExecutorTests : IDisposable
{
    // ── Test infrastructure ───────────────────────────────────────────────────

    private const string TestSubKey = @"Software\OptimizerPluginTest";
    private const string TestValueName = "PluginTestValue";

    private readonly Mock<IUndoService> _undoMock;
    private readonly DeclarativeChangeExecutor _executor;

    // Track CaptureRegistry calls for assertion
    private readonly List<(string root, string subKey, string valueName, string description)> _capturedRegistryCalls = new();
    // Track entries added via AddEntry
    private readonly List<UndoEntry> _addedEntries = new();

    public DeclarativeChangeExecutorTests()
    {
        _undoMock = new Mock<IUndoService>();

        // Intercept CaptureRegistry calls
        _undoMock
            .Setup(u => u.CaptureRegistry(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((root, subKey, valueName, description) =>
                _capturedRegistryCalls.Add((root, subKey, valueName, description)));

        // SaveAsync is a no-op in tests
        _undoMock
            .Setup(u => u.SaveAsync())
            .Returns(Task.CompletedTask);

        _executor = new DeclarativeChangeExecutor(_undoMock.Object);

        // Clean up any leftover test key from previous runs
        CleanTestKey();
    }

    public void Dispose()
    {
        CleanTestKey();
        GC.SuppressFinalize(this);
    }

    private static void CleanTestKey()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestSubKey, throwOnMissingSubKey: false);
        }
        catch { /* best-effort */ }
    }

    private static OptimizationManifest MakeManifestWithRegistry(
        string path = @"HKCU\Software\OptimizerPluginTest",
        string valueName = TestValueName,
        string applyValue = "1",
        string valueType = "dword")
        => new()
        {
            ManifestVersion = 1,
            Id = "test-plugin-manifest",
            Name = "Test Plugin",
            Category = "System",
            Changes =
            {
                new ManifestChange
                {
                    Type = "registry",
                    Path = path,
                    Value = valueName,
                    ValueType = valueType,
                    Apply = applyValue,
                    Revert = "0"
                }
            }
        };

    // ── ManifestPermissions.IsRegistryPathAllowed ─────────────────────────────

    [Fact]
    public void IsRegistryPathAllowed_AllowedHkcu_ReturnsTrue()
    {
        Assert.True(ManifestPermissions.IsRegistryPathAllowed(@"HKCU\Software\MyApp\Setting"));
    }

    [Fact]
    public void IsRegistryPathAllowed_AllowedHklmPolicies_ReturnsTrue()
    {
        Assert.True(ManifestPermissions.IsRegistryPathAllowed(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Foo"));
    }

    [Fact]
    public void IsRegistryPathAllowed_DisallowedPath_ReturnsFalse()
    {
        // HKLM\SYSTEM\...\Control is outside the allow-list
        Assert.False(ManifestPermissions.IsRegistryPathAllowed(@"HKLM\SYSTEM\CurrentControlSet\Control\Foo"));
    }

    // ── ManifestPermissions.IsFilePathAllowed ─────────────────────────────────

    [Fact]
    public void IsFilePathAllowed_TempEnvVar_ReturnsTrue()
    {
        var tempPath = Environment.GetEnvironmentVariable("TEMP") + @"\somecachefile.tmp";
        Assert.True(ManifestPermissions.IsFilePathAllowed(tempPath));
    }

    [Fact]
    public void IsFilePathAllowed_System32_ReturnsFalse()
    {
        Assert.False(ManifestPermissions.IsFilePathAllowed(@"C:\Windows\System32\drivers\etc\hosts"));
    }

    // ── ValidatePermissions ───────────────────────────────────────────────────

    [Fact]
    public void ValidatePermissions_AllowedRegistryPath_ReturnsTrue()
    {
        var manifest = MakeManifestWithRegistry(@"HKCU\Software\OptimizerPluginTest");
        var ok = _executor.ValidatePermissions(manifest, out var violations);

        Assert.True(ok);
        Assert.Empty(violations);
    }

    [Fact]
    public void ValidatePermissions_DisallowedRegistryPath_ReturnsFalseWithViolation()
    {
        var manifest = MakeManifestWithRegistry(@"HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders");
        var ok = _executor.ValidatePermissions(manifest, out var violations);

        Assert.False(ok);
        Assert.NotEmpty(violations);
        Assert.Contains(violations, v => v.Contains("outside the permitted allow-list", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatePermissions_DisallowedFilePath_ReturnsFalseWithViolation()
    {
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-file-plugin",
            Name = "Bad File Plugin",
            Category = "Storage",
            Changes =
            {
                new ManifestChange
                {
                    Type = "file",
                    FilePath = @"C:\Windows\System32\evil.dll",
                    FileAction = "delete"
                }
            }
        };

        var ok = _executor.ValidatePermissions(manifest, out var violations);
        Assert.False(ok);
        Assert.NotEmpty(violations);
    }

    [Fact]
    public void ValidatePermissions_AllowedTempFilePath_ReturnsTrue()
    {
        var tempPath = Environment.GetEnvironmentVariable("TEMP") + @"\optimizer-test-cache";
        var manifest = new OptimizationManifest
        {
            ManifestVersion = 1,
            Id = "test-file-plugin",
            Name = "Temp File Plugin",
            Category = "Storage",
            Changes =
            {
                new ManifestChange
                {
                    Type = "file",
                    FilePath = tempPath,
                    FileAction = "delete"
                }
            }
        };

        var ok = _executor.ValidatePermissions(manifest, out var violations);
        Assert.True(ok);
        Assert.Empty(violations);
    }

    // ── IsApplied ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsApplied_RegistryMatchesApplyValue_ReturnsTrue()
    {
        // Write the "applied" value directly so IsApplied can read it
        using var key = Registry.CurrentUser.CreateSubKey(TestSubKey);
        key.SetValue(TestValueName, 1, RegistryValueKind.DWord);

        var manifest = MakeManifestWithRegistry(applyValue: "1");
        Assert.True(_executor.IsApplied(manifest));
    }

    [Fact]
    public void IsApplied_RegistryDiffersFromApplyValue_ReturnsFalse()
    {
        // Write "0" — different from apply value "1"
        using var key = Registry.CurrentUser.CreateSubKey(TestSubKey);
        key.SetValue(TestValueName, 0, RegistryValueKind.DWord);

        var manifest = MakeManifestWithRegistry(applyValue: "1");
        Assert.False(_executor.IsApplied(manifest));
    }

    // ── ApplyAsync (registry) ─────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_RegistryChange_SetsValueInRegistry()
    {
        var manifest = MakeManifestWithRegistry(applyValue: "42");

        var result = await _executor.ApplyAsync(manifest);

        Assert.True(result.Success, result.Message);

        // Confirm the registry value was actually written
        using var key = Registry.CurrentUser.OpenSubKey(TestSubKey);
        var written = key?.GetValue(TestValueName);
        Assert.Equal(42, written);
    }

    [Fact]
    public async Task ApplyAsync_RegistryChange_CapturesUndoViaUndoService()
    {
        var manifest = MakeManifestWithRegistry(applyValue: "7");

        await _executor.ApplyAsync(manifest);

        // CaptureRegistry must have been called once
        _undoMock.Verify(
            u => u.CaptureRegistry("HKCU", TestSubKey, TestValueName, It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_RegistryChange_UndoRestoresPriorValue()
    {
        // Use the real UndoService to test the end-to-end undo round-trip
        var realUndo = new UndoService();

        // Pre-set a known value that UndoService should capture
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key.SetValue(TestValueName, 99, RegistryValueKind.DWord);

        var executor = new DeclarativeChangeExecutor(realUndo);
        var manifest = MakeManifestWithRegistry(applyValue: "1");

        // Apply (captures prior value 99 into undo log)
        var applyResult = await executor.ApplyAsync(manifest);
        Assert.True(applyResult.Success, applyResult.Message);

        // Confirm the value is now 1
        using (var key = Registry.CurrentUser.OpenSubKey(TestSubKey))
            Assert.Equal(1, (int)key!.GetValue(TestValueName)!);

        // Undo all — should restore 99
        await realUndo.UndoAllAsync();

        using (var key = Registry.CurrentUser.OpenSubKey(TestSubKey))
            Assert.Equal(99, (int)key!.GetValue(TestValueName)!);
    }

    [Fact]
    public async Task ApplyAsync_RegistryUndo_DeletesValueThatDidNotExistBefore()
    {
        // Ensure the value does NOT exist before apply
        CleanTestKey();

        var realUndo = new UndoService();
        var executor = new DeclarativeChangeExecutor(realUndo);
        var manifest = MakeManifestWithRegistry(applyValue: "5");

        await executor.ApplyAsync(manifest);

        // Value now exists
        using (var key = Registry.CurrentUser.OpenSubKey(TestSubKey))
            Assert.NotNull(key?.GetValue(TestValueName));

        // Undo — value should be gone (deleted)
        await realUndo.UndoAllAsync();

        using (var key = Registry.CurrentUser.OpenSubKey(TestSubKey))
        {
            // Either key doesn't exist, or value was deleted
            var val = key?.GetValue(TestValueName);
            Assert.Null(val);
        }
    }

    [Fact]
    public async Task ApplyAsync_PermissionViolation_ReturnsFail_MakesNoChanges()
    {
        var manifest = MakeManifestWithRegistry(@"HKLM\SYSTEM\CurrentControlSet\Control\BadKey");

        var result = await _executor.ApplyAsync(manifest);

        Assert.False(result.Success);
        Assert.Contains("Permission violations", result.Message, StringComparison.Ordinal);

        // CaptureRegistry must NOT have been called
        _undoMock.Verify(
            u => u.CaptureRegistry(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }
}
