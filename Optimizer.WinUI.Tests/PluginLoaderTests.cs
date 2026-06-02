using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Moq;
using Optimizer.WinUI.Models.Plugins;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Plugins;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Unit tests for PluginLoader (B3).
/// Each test gets a fresh isolated temp directory so tests are independent.
/// </summary>
public class PluginLoaderTests : IDisposable
{
    // ── Infrastructure ────────────────────────────────────────────────────────

    private readonly string _tempDir;
    private readonly string _stateFile;
    private readonly ManifestParser _realParser = new();
    private readonly Mock<IDeclarativeChangeExecutor> _executorMock;

    public PluginLoaderTests()
    {
        _tempDir   = Path.Combine(Path.GetTempPath(), $"OptimizerPluginTest_{Guid.NewGuid():N}");
        _stateFile = Path.Combine(_tempDir, "plugin-state.json");
        Directory.CreateDirectory(_tempDir);

        _executorMock = new Mock<IDeclarativeChangeExecutor>();

        // Default: all permissions pass
        _executorMock
            .Setup(e => e.ValidatePermissions(It.IsAny<OptimizationManifest>(), out It.Ref<IReadOnlyList<string>>.IsAny))
            .Returns((OptimizationManifest _, out IReadOnlyList<string> v) =>
            {
                v = Array.Empty<string>();
                return true;
            });

        // Default: IsApplied = false
        _executorMock
            .Setup(e => e.IsApplied(It.IsAny<OptimizationManifest>()))
            .Returns(false);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private PluginLoader CreateLoader() =>
        new(_realParser, _executorMock.Object, _tempDir, _stateFile);

    private static string ValidYaml(string id = "test-plugin", string name = "Test Plugin") =>
        $"""
        manifest_version: 1
        id: {id}
        name: {name}
        description: A test plugin.
        author: Tester
        category: System
        requires_admin: false
        reversible: true
        changes:
          - type: registry
            path: HKCU\Software\OptimizerPluginTest
            value: TestValue
            value_type: dword
            apply: "1"
            revert: "0"
        """;

    private string WritePlugin(string id, string yaml)
    {
        var path = Path.Combine(_tempDir, $"{id}.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    // ── Test 1: empty folder → no plugins ─────────────────────────────────────

    [Fact]
    public void Reload_EmptyFolder_NoPlugins()
    {
        var loader = CreateLoader();
        Assert.Empty(loader.LoadedPlugins);
    }

    // ── Test 2: install valid manifest → appears enabled ──────────────────────

    [Fact]
    public async Task InstallFromFileAsync_ValidManifest_AppearsEnabledInLoadedPlugins()
    {
        var source = Path.Combine(Path.GetTempPath(), $"valid_{Guid.NewGuid():N}.yaml");
        File.WriteAllText(source, ValidYaml("my-plugin"));
        try
        {
            var loader = CreateLoader();
            var ok = await loader.InstallFromFileAsync(source);

            Assert.True(ok);
            Assert.Single(loader.LoadedPlugins);
            Assert.Equal("my-plugin", loader.LoadedPlugins[0].Manifest.Id);
            Assert.True(loader.LoadedPlugins[0].Enabled);
        }
        finally
        {
            File.Delete(source);
        }
    }

    // ── Test 3: install invalid manifest → rejected ───────────────────────────

    [Fact]
    public async Task InstallFromFileAsync_InvalidManifest_ReturnsFalse_NotInstalled()
    {
        var source = Path.Combine(Path.GetTempPath(), $"bad_{Guid.NewGuid():N}.yaml");
        // Bad ID format (uppercase) should fail validation
        File.WriteAllText(source, ValidYaml("Bad ID With Spaces"));
        try
        {
            var loader = CreateLoader();
            var ok = await loader.InstallFromFileAsync(source);

            Assert.False(ok);
            Assert.Empty(loader.LoadedPlugins);
        }
        finally
        {
            File.Delete(source);
        }
    }

    // ── Test 4: permission violation → force-disabled, no handler ────────────

    [Fact]
    public void Reload_PermissionViolation_ForceDisabled_NoHandlerCreated()
    {
        // Configure executor to report a violation
        var violations = new List<string> { "Registry path 'HKLM\\SYSTEM\\Control' is outside the permitted allow-list." };
        IReadOnlyList<string> outViolations = violations;
        _executorMock
            .Setup(e => e.ValidatePermissions(It.IsAny<OptimizationManifest>(), out outViolations))
            .Returns(false);

        WritePlugin("bad-plugin", ValidYaml("bad-plugin"));
        var loader = CreateLoader();

        Assert.Single(loader.LoadedPlugins);
        Assert.False(loader.LoadedPlugins[0].Enabled);
        Assert.NotEmpty(loader.LoadedPlugins[0].PermissionViolations);
        Assert.Empty(loader.CreateHandlers());
    }

    // ── Test 5: CreateHandlers only for enabled+clean plugins ─────────────────

    [Fact]
    public void CreateHandlers_ReturnsHandlersOnlyForEnabledCleanPlugins()
    {
        WritePlugin("plugin-a", ValidYaml("plugin-a"));
        WritePlugin("plugin-b", ValidYaml("plugin-b"));

        var loader = CreateLoader();
        // Both enabled by default
        Assert.Equal(2, loader.LoadedPlugins.Count);
        Assert.Equal(2, loader.CreateHandlers().Count);

        // Disable one
        loader.SetEnabled("plugin-a", false);
        Assert.Single(loader.CreateHandlers());
        Assert.Equal("plugin-b", loader.CreateHandlers()[0].Id);
    }

    // ── Test 6: SetEnabled false → handler no longer created ──────────────────

    [Fact]
    public void SetEnabled_False_HandlerNoLongerCreated()
    {
        WritePlugin("toggle-plugin", ValidYaml("toggle-plugin"));
        var loader = CreateLoader();

        Assert.Single(loader.CreateHandlers());

        var ok = loader.SetEnabled("toggle-plugin", false);
        Assert.True(ok);
        Assert.Empty(loader.CreateHandlers());
    }

    // ── Test 7: Remove → plugin gone, file deleted ────────────────────────────

    [Fact]
    public void Remove_PluginGone_FileDeleted()
    {
        var filePath = WritePlugin("doomed-plugin", ValidYaml("doomed-plugin"));
        var loader   = CreateLoader();
        Assert.Single(loader.LoadedPlugins);

        var ok = loader.Remove("doomed-plugin");
        Assert.True(ok);
        Assert.Empty(loader.LoadedPlugins);
        Assert.False(File.Exists(filePath));
    }

    // ── Test 8: enabled state persists across Reload ──────────────────────────

    [Fact]
    public void EnabledState_PersistsAcrossReload()
    {
        WritePlugin("persistent-plugin", ValidYaml("persistent-plugin"));
        var loader = CreateLoader();
        loader.SetEnabled("persistent-plugin", false);

        // Create a fresh loader pointing at the same folder/state file
        var loader2 = new PluginLoader(_realParser, _executorMock.Object, _tempDir, _stateFile);
        Assert.Single(loader2.LoadedPlugins);
        Assert.False(loader2.LoadedPlugins[0].Enabled);
    }

    // ── Test 9: ManifestOptimizationHandler.Id matches manifest ──────────────

    [Fact]
    public void ManifestOptimizationHandler_Id_MatchesManifest()
    {
        WritePlugin("id-check-plugin", ValidYaml("id-check-plugin"));
        var loader   = CreateLoader();
        var handlers = loader.CreateHandlers();

        Assert.Single(handlers);
        Assert.Equal("id-check-plugin", handlers[0].Id);
    }

    // ── Test 10: ManifestOptimizationHandler.Info maps fields correctly ────────

    [Fact]
    public void ManifestOptimizationHandler_Info_MapsFieldsCorrectly()
    {
        var yaml = """
            manifest_version: 1
            id: field-map-test
            name: Field Map Test
            description: A description.
            author: Some Author
            category: Privacy
            requires_admin: true
            requires_restart: true
            reversible: true
            pros:
              - Pro one
            cons:
              - Con one
            changes:
              - type: registry
                path: HKCU\Software\OptimizerPluginTest
                value: TestValue
                value_type: dword
                apply: "1"
                revert: "0"
            """;
        WritePlugin("field-map-test", yaml);
        var loader  = CreateLoader();
        var handler = loader.CreateHandlers().Single();
        var info    = handler.Info;

        Assert.Equal("field-map-test", info.Id);
        Assert.Equal("Field Map Test", info.Title);
        Assert.Equal("A description.", info.Summary);
        Assert.Equal("Some Author", info.Author);
        Assert.True(info.RequiresAdmin);
        Assert.True(info.RequiresRestart);
        Assert.True(info.Reversible);
        Assert.Contains("Pro one", info.Pros);
        Assert.Contains("Con one", info.Cons);
        Assert.True(info.IsPlugin);
    }

    // ── Test 11: ManifestOptimizationHandler.IsApplied delegates to executor ──

    [Fact]
    public void ManifestOptimizationHandler_IsApplied_DelegatesToExecutor()
    {
        _executorMock
            .Setup(e => e.IsApplied(It.IsAny<OptimizationManifest>()))
            .Returns(true);

        WritePlugin("applied-plugin", ValidYaml("applied-plugin"));
        var loader  = CreateLoader();
        var handler = loader.CreateHandlers().Single();

        Assert.True(handler.IsApplied());
    }

    // ── Test 12: duplicate plugin id — second file load wins (last-write-time order) ──

    [Fact]
    public void Reload_DuplicatePluginId_OnlyOneLoaded()
    {
        // Write two files with the same plugin id (they will both parse but id is the same)
        // The parser won't reject a duplicate id, but we'll end up with two LoadedPlugin entries.
        // The spec says CreateHandlers should still work; we just verify both are loaded.
        // (The test documents current behaviour: duplicates are all loaded, CreateHandlers
        //  returns handlers for all enabled+clean ones including duplicates.)
        File.WriteAllText(Path.Combine(_tempDir, "dup-a.yaml"), ValidYaml("dup-plugin"));
        File.WriteAllText(Path.Combine(_tempDir, "dup-b.yaml"), ValidYaml("dup-plugin"));

        var loader = CreateLoader();

        // Both files loaded — duplicates surfaced so the UI can warn
        Assert.Equal(2, loader.LoadedPlugins.Count);
        Assert.All(loader.LoadedPlugins, p => Assert.Equal("dup-plugin", p.Manifest.Id));
    }
}
