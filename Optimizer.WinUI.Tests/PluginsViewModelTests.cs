using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using Optimizer.WinUI.Models.Plugins;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Cloud;
using Optimizer.WinUI.Services.Events;
using Optimizer.WinUI.Services.Optimizations;
using Optimizer.WinUI.Services.Plugins;
using Optimizer.WinUI.ViewModels;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Unit tests for PluginsViewModel using mocked dependencies.
/// Tests that do not require calling RefreshHandlers (which requires a concrete
/// WindowsOptimizerService) are covered here.  The install/toggle/remove flows
/// that mutate optimizer state are covered by integration tests.
/// </summary>
public class PluginsViewModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OptimizationManifest MakeManifest(string id = "test-plugin") => new()
    {
        ManifestVersion = 1,
        Id              = id,
        Name            = $"Test {id}",
        Description     = "A test plugin",
        Author          = "Tester",
        Category        = "Privacy",
        Changes         = []
    };

    private static LoadedPlugin MakeLoaded(string id = "test-plugin", bool enabled = true) =>
        new(MakeManifest(id), $"C:\\plugins\\{id}.yaml", enabled, []);

    private static LoadedPlugin MakeBlockedPlugin(string id = "blocked-plugin") =>
        new(MakeManifest(id), $"C:\\plugins\\{id}.yaml", false,
            ["Registry path 'HKLM\\SYSTEM\\Control' is disallowed"]);

    // Build a minimal PluginsViewModel using reflection to avoid the cast requirement in production.
    // We inject a mock IWindowsOptimizerService and override the optimizer field via a test subclass.
    private static PluginsViewModelTestable Create(
        IReadOnlyList<LoadedPlugin>? plugins = null,
        bool isAuthenticated = false,
        string? serverUrl = null)
    {
        var loaderMock   = new Mock<IPluginLoader>();
        var cloudMock    = new Mock<IOptimizerCloudClient>();
        var verifierMock = new Mock<IPluginVerifier>();
        var parserMock   = new Mock<IManifestParser>();

        loaderMock.Setup(l => l.LoadedPlugins).Returns(plugins ?? []);
        loaderMock.Setup(l => l.PluginsFolder).Returns("C:\\plugins");

        cloudMock.Setup(c => c.IsAuthenticated).Returns(isAuthenticated);
        cloudMock.Setup(c => c.ServerUrl).Returns(serverUrl);

        verifierMock.Setup(v => v.Verify(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new VerificationResult(false, "unsigned (community plugin)"));

        parserMock.Setup(p => p.ParseYaml(It.IsAny<string>()))
            .Returns(new ManifestParseResult(true, MakeManifest(), []));

        return new PluginsViewModelTestable(
            loaderMock.Object, cloudMock.Object, verifierMock.Object, parserMock.Object);
    }

    // ── Tests: installed list ─────────────────────────────────────────────────

    [Fact]
    public void InstalledList_ReflectsLoaderPlugins_AfterRefresh()
    {
        var plugins = new[]
        {
            MakeLoaded("plugin-a", enabled: true),
            MakeLoaded("plugin-b", enabled: false)
        };
        var vm = Create(plugins);
        vm.RefreshInstalledCommand.Execute(null);

        Assert.Equal(2, vm.Installed.Count);
        Assert.Equal("plugin-a", vm.Installed[0].PluginId);
        Assert.True(vm.Installed[0].IsEnabled);
        Assert.Equal("plugin-b", vm.Installed[1].PluginId);
        Assert.False(vm.Installed[1].IsEnabled);
    }

    [Fact]
    public void BlockedPlugin_HasViolations_Flag()
    {
        var plugins = new[] { MakeBlockedPlugin("blocked") };
        var vm = Create(plugins);
        vm.RefreshInstalledCommand.Execute(null);

        var item = vm.Installed.Single();
        Assert.True(item.HasViolations);
        Assert.False(item.IsEnabled);
    }

    // ── Tests: CanSubmit ──────────────────────────────────────────────────────

    [Fact]
    public void CanSubmit_False_WhenNotAuthenticated()
    {
        var vm = Create(isAuthenticated: false);
        Assert.False(vm.CanSubmit);
    }

    [Fact]
    public void CanSubmit_True_WhenAuthenticated()
    {
        var vm = Create(isAuthenticated: true);
        Assert.True(vm.CanSubmit);
    }

    // ── Tests: server URL / empty states ─────────────────────────────────────

    [Fact]
    public void ShowNoServer_True_WhenNoServerUrl()
    {
        var vm = Create(serverUrl: null);
        Assert.True(vm.ShowNoServer);
    }

    [Fact]
    public void ShowNoServer_False_WhenServerUrlSet()
    {
        var vm = Create(serverUrl: "http://localhost:5000");
        Assert.False(vm.ShowNoServer);
    }

    [Fact]
    public void IsInstalledEmpty_True_WhenNoPlugins()
    {
        var vm = Create([]);
        vm.RefreshInstalledCommand.Execute(null);

        Assert.True(vm.IsInstalledEmpty);
    }

    [Fact]
    public void IsInstalledEmpty_False_WhenPluginsExist()
    {
        var plugins = new[] { MakeLoaded("p1") };
        var vm = Create(plugins);
        vm.RefreshInstalledCommand.Execute(null);

        Assert.False(vm.IsInstalledEmpty);
    }

    // ── Tests: InstalledPluginVm from ────────────────────────────────────────

    [Fact]
    public void InstalledPluginVm_From_MapsFields()
    {
        var plugin = MakeLoaded("map-test", enabled: true);
        var vm     = InstalledPluginVm.From(plugin);

        Assert.Equal("map-test", vm.PluginId);
        Assert.Equal("Test map-test", vm.Name);
        Assert.True(vm.IsEnabled);
        Assert.Empty(vm.PermissionViolations);
        Assert.False(vm.HasViolations);
    }

    [Fact]
    public void RemotePluginVm_DownloadsText_FormatsK()
    {
        var vm = new RemotePluginVm { Downloads = 12500 };
        Assert.Equal("12.5K", vm.DownloadsText);
    }

    [Fact]
    public void RemotePluginVm_DownloadsText_SmallNumber()
    {
        var vm = new RemotePluginVm { Downloads = 42 };
        Assert.Equal("42", vm.DownloadsText);
    }
}

/// <summary>
/// Testable subclass that replaces the <see cref="WindowsOptimizerService"/> cast
/// with a no-op so unit tests can exercise the VM without the full DI graph.
/// </summary>
internal sealed class PluginsViewModelTestable : PluginsViewModel
{
    // We need to pass a concrete WindowsOptimizerService, but constructing one requires
    // many dependencies. Instead, we pass null and guard with a sentinel.
    // The ViewModel stores the cast result in _optimizer; in tests, RefreshHandlers
    // is never called because we're not exercising toggle/remove commands here.
    // To avoid a null-ref in the base constructor we pass a real (empty) service.
    public PluginsViewModelTestable(
        IPluginLoader loader,
        IOptimizerCloudClient cloud,
        IPluginVerifier verifier,
        IManifestParser parser)
        : base(loader, cloud, verifier, parser,
               new NoOpWindowsOptimizerService(loader))
    { }
}

/// <summary>Minimal WindowsOptimizerService for test injection — overrides RefreshHandlers.</summary>
internal sealed class NoOpWindowsOptimizerService : WindowsOptimizerService
{
    public NoOpWindowsOptimizerService(IPluginLoader loader)
        : base(
            handlers: [],
            pluginLoader: MakeLoader(loader),
            undoService: new Mock<IUndoService>().Object,
            elevationService: new Mock<IElevationService>().Object,
            monitorService: new Mock<ISystemMonitorService>().Object,
            startupService: new Mock<IStartupService>().Object,
            eventBus: new Mock<IEventBus>().Object)
    { }

    public new void RefreshHandlers() { /* no-op in tests */ }

    private static IPluginLoader MakeLoader(IPluginLoader original)
    {
        // Ensure CreateHandlers() returns an empty list so the base constructor doesn't NRE
        var mock = new Mock<IPluginLoader>();
        mock.Setup(l => l.LoadedPlugins).Returns(original.LoadedPlugins);
        mock.Setup(l => l.CreateHandlers()).Returns([]);
        mock.Setup(l => l.PluginsFolder).Returns("C:\\test\\plugins");
        return mock.Object;
    }
}
