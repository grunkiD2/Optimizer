using System.IO;
using System.Threading.Tasks;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for the safety-critical undo/restore path.
/// UndoService persists to %LocalAppData%\Optimizer\undo.json so tests operate
/// against that real path. A future path-injection refactor would allow full isolation.
/// </summary>
public class UndoServiceTests
{
    // Isolate every test on its own temp store so the suite never mutates the user's real undo.json.
    private static UndoService NewService() => new(Path.GetTempFileName());

    [Fact]
    public void Count_StartsAtZero()
    {
        var service = NewService();
        // A freshly constructed service has no entries (Load has not been called)
        Assert.Equal(0, service.Count);
    }

    [Fact]
    public void CaptureRegistry_IncrementsCount()
    {
        var service = NewService();

        // CaptureRegistry reads HKCU — safe to call even without elevation
        service.CaptureRegistry(
            root: "HKCU",
            subKey: @"Software\Optimizer\TestKey",
            valueName: "TestValue",
            description: "UndoServiceTests: CaptureRegistry_IncrementsCount");

        Assert.Equal(1, service.Count);
    }

    [Fact]
    public void Entries_ReflectsCapturedItems()
    {
        var service = NewService();

        service.CaptureRegistry(
            root: "HKCU",
            subKey: @"Software\Optimizer\TestKey",
            valueName: "Foo",
            description: "entry-a");

        service.CaptureRegistry(
            root: "HKCU",
            subKey: @"Software\Optimizer\TestKey",
            valueName: "Bar",
            description: "entry-b");

        Assert.Equal(2, service.Count);
        Assert.Contains(service.Entries, e => e.Description == "entry-a");
        Assert.Contains(service.Entries, e => e.Description == "entry-b");
    }

    [Fact]
    public void CapturePowerScheme_IncrementsCount()
    {
        var service = NewService();

        service.CapturePowerScheme(
            previousGuid: "381b4222-f694-41f0-9685-ff5bb260df2e",
            description: "Test power scheme capture");

        Assert.Equal(1, service.Count);
        Assert.Equal(UndoActionKind.ActivePowerScheme, service.Entries[0].Kind);
    }

    [Fact]
    public async Task SaveAsync_DoesNotThrow()
    {
        var service = NewService();
        service.CaptureRegistry("HKCU", @"Software\Optimizer\TestKey", "Tmp", "save test");

        // Should not throw even if the directory exists or doesn't exist
        await service.SaveAsync();
    }

    [Fact]
    public async Task UndoAllAsync_ClearsEntries()
    {
        var service = NewService();
        service.CaptureRegistry("HKCU", @"Software\Optimizer\TestKey", "Nonexistent", "undo-all test");
        Assert.Equal(1, service.Count);

        // UndoAllAsync will attempt a restore; it won't crash for a non-existent key
        await service.UndoAllAsync();

        Assert.Equal(0, service.Count);
    }
}
