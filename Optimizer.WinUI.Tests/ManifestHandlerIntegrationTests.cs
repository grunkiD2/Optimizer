using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Models.Plugins;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Events;
using Optimizer.WinUI.Services.Optimizations;
using Optimizer.WinUI.Services.Plugins;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Integration tests for ManifestOptimizationHandler + PluginLoader (B3).
///
/// Registry tests use HKCU\Software\OptimizerPluginTest — deleted in Dispose.
/// </summary>
[Collection("RegistryTests")]
public class ManifestHandlerIntegrationTests : IDisposable
{
    private const string TestSubKey    = @"Software\OptimizerPluginTest";
    private const string TestValueName = "ManifestHandlerTestValue";

    private readonly string _tempDir;
    private readonly string _stateFile;
    private readonly ManifestParser  _parser      = new();
    private readonly UndoService     _realUndo    = new();
    private readonly DeclarativeChangeExecutor _realExecutor;

    public ManifestHandlerIntegrationTests()
    {
        _tempDir   = Path.Combine(Path.GetTempPath(), $"OptimizerHandlerTest_{Guid.NewGuid():N}");
        _stateFile = Path.Combine(_tempDir, "plugin-state.json");
        Directory.CreateDirectory(_tempDir);

        _realExecutor = new DeclarativeChangeExecutor(_realUndo);
        CleanTestKey();
    }

    public void Dispose()
    {
        CleanTestKey();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    private static void CleanTestKey()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(TestSubKey, throwOnMissingSubKey: false); }
        catch { /* best-effort */ }
    }

    // ── Helper: a valid manifest that writes to our sacrificial HKCU key ──────

    private OptimizationManifest MakeTestManifest(string id = "integration-test-plugin") => new()
    {
        ManifestVersion = 1,
        Id          = id,
        Name        = "Integration Test Plugin",
        Description = "Writes to a sacrificial registry key.",
        Author      = "Test",
        Category    = "System",
        RequiresAdmin = false,
        Reversible    = true,
        Changes       =
        {
            new ManifestChange
            {
                Type      = "registry",
                Path      = @"HKCU\Software\OptimizerPluginTest",
                Value     = TestValueName,
                ValueType = "dword",
                Apply     = "42",
                Revert    = "0"
            }
        }
    };

    // ── Test 1: ApplyAsync writes the registry value ───────────────────────────

    [Fact]
    public async Task ApplyAsync_WritesRegistryValue()
    {
        var handler = new ManifestOptimizationHandler(MakeTestManifest(), _realExecutor);
        var undoMock = new Mock<IUndoService>();
        undoMock.Setup(u => u.CaptureRegistry(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()));
        undoMock.Setup(u => u.SaveAsync()).Returns(Task.CompletedTask);

        // Use a fresh executor with the mock undo for this sub-test
        var executor = new DeclarativeChangeExecutor(undoMock.Object);
        var handler2 = new ManifestOptimizationHandler(MakeTestManifest("write-test"), executor);
        var elevMock = new Mock<IElevationService>();
        elevMock.Setup(e => e.IsElevated).Returns(true);

        var result = await handler2.ApplyAsync(undoMock.Object, elevMock.Object);

        Assert.True(result.Success, result.Message);

        using var key = Registry.CurrentUser.OpenSubKey(TestSubKey);
        Assert.Equal(42, (int)key!.GetValue(TestValueName)!);
    }

    // ── Test 2: After apply, IsApplied returns true ───────────────────────────

    [Fact]
    public async Task IsApplied_AfterApply_ReturnsTrue()
    {
        var undoMock = new Mock<IUndoService>();
        undoMock.Setup(u => u.CaptureRegistry(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()));
        undoMock.Setup(u => u.SaveAsync()).Returns(Task.CompletedTask);

        var executor = new DeclarativeChangeExecutor(undoMock.Object);
        var manifest = MakeTestManifest("is-applied-test");
        var handler  = new ManifestOptimizationHandler(manifest, executor);
        var elevMock = new Mock<IElevationService>();

        // Before apply
        Assert.NotEqual(true, handler.IsApplied());

        await handler.ApplyAsync(undoMock.Object, elevMock.Object);

        // After apply
        Assert.True(handler.IsApplied());
    }

    // ── Test 3: Plugin appears in WindowsOptimizerService merged set ──────────

    [Fact]
    public async Task Plugin_AppearsInWindowsOptimizerServiceMergedSet()
    {
        // Write a plugin manifest to temp folder
        var yaml = """
            manifest_version: 1
            id: merge-test-plugin
            name: Merge Test Plugin
            description: Tests that plugins appear in the optimizer service.
            author: Test
            category: System
            requires_admin: false
            reversible: true
            changes:
              - type: registry
                path: HKCU\Software\OptimizerPluginTest
                value: MergeTestValue
                value_type: dword
                apply: "1"
                revert: "0"
            """;
        File.WriteAllText(Path.Combine(_tempDir, "merge-test-plugin.yaml"), yaml);

        var executorMock = new Mock<IDeclarativeChangeExecutor>();
        IReadOnlyList<string> noViolations = Array.Empty<string>();
        executorMock
            .Setup(e => e.ValidatePermissions(It.IsAny<OptimizationManifest>(), out noViolations))
            .Returns(true);
        executorMock
            .Setup(e => e.IsApplied(It.IsAny<OptimizationManifest>()))
            .Returns(false);

        var loader = new PluginLoader(_parser, executorMock.Object, _tempDir, _stateFile);

        // Build a minimal WindowsOptimizerService with no built-in handlers
        var undoMock   = new Mock<IUndoService>();
        var elevMock   = new Mock<IElevationService>();
        var monMock    = new Mock<ISystemMonitorService>();
        monMock.Setup(m => m.CollectSnapshot()).Returns(new SystemResource());
        monMock.Setup(m => m.GetResourceHistoryAsync(It.IsAny<int>())).ReturnsAsync(Array.Empty<SystemResource>());
        var startupMock = new Mock<IStartupService>();
        startupMock.Setup(s => s.GetEntries()).Returns(new List<StartupEntry>());

        var service = new WindowsOptimizerService(
            Enumerable.Empty<IOptimizationHandler>(),
            loader,
            undoMock.Object,
            elevMock.Object,
            monMock.Object,
            startupMock.Object,
            new Mock<IEventBus>().Object);

        var ids = (await service.GetAvailableOptimizationsAsync()).ToList();
        Assert.Contains("merge-test-plugin", ids, StringComparer.OrdinalIgnoreCase);
    }

    // ── Test 4: Built-in id collision → built-in wins ─────────────────────────

    [Fact]
    public void BuiltInIdCollision_BuiltInWins()
    {
        // Write a plugin whose id matches a built-in handler
        var yaml = """
            manifest_version: 1
            id: builtin-collision-id
            name: Collision Plugin
            description: Same id as a built-in.
            author: Test
            category: System
            requires_admin: false
            reversible: true
            changes:
              - type: registry
                path: HKCU\Software\OptimizerPluginTest
                value: CollisionValue
                value_type: dword
                apply: "1"
                revert: "0"
            """;
        File.WriteAllText(Path.Combine(_tempDir, "builtin-collision-id.yaml"), yaml);

        var executorMock = new Mock<IDeclarativeChangeExecutor>();
        IReadOnlyList<string> noViolations = Array.Empty<string>();
        executorMock
            .Setup(e => e.ValidatePermissions(It.IsAny<OptimizationManifest>(), out noViolations))
            .Returns(true);

        var loader = new PluginLoader(_parser, executorMock.Object, _tempDir, _stateFile);

        // Built-in handler with the same id
        var builtInMock = new Mock<IOptimizationHandler>();
        builtInMock.Setup(h => h.Id).Returns("builtin-collision-id");
        builtInMock.Setup(h => h.Info).Returns(new OptimizationInfo { Id = "builtin-collision-id", Title = "Built-in", IsPlugin = false });

        var undoMock    = new Mock<IUndoService>();
        var elevMock    = new Mock<IElevationService>();
        var monMock     = new Mock<ISystemMonitorService>();
        monMock.Setup(m => m.CollectSnapshot()).Returns(new SystemResource());
        monMock.Setup(m => m.GetResourceHistoryAsync(It.IsAny<int>())).ReturnsAsync(Array.Empty<SystemResource>());
        var startupMock = new Mock<IStartupService>();
        startupMock.Setup(s => s.GetEntries()).Returns(new List<StartupEntry>());

        var service = new WindowsOptimizerService(
            new[] { builtInMock.Object },
            loader,
            undoMock.Object,
            elevMock.Object,
            monMock.Object,
            startupMock.Object,
            new Mock<IEventBus>().Object);

        // The handler registered should be the built-in (IsPlugin = false)
        var info = service.GetOptimizationInfo("builtin-collision-id");
        Assert.NotNull(info);
        Assert.False(info!.IsPlugin, "Built-in handler should win over plugin with same ID.");
    }
}
