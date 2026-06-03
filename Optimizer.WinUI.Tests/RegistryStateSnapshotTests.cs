using System;
using Microsoft.Win32;
using Optimizer.WinUI.Services.Data;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Round-trips registry values through capture/restore under a disposable HKCU test key.
/// </summary>
public class RegistryStateSnapshotTests : IDisposable
{
    private const string TestSubKey = @"Software\OptimizerTest_Snapshot";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(TestSubKey, throwOnMissingSubKey: false);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Restore_reverts_a_modified_dword_to_its_captured_value()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue("Mode", 1, RegistryValueKind.DWord);

        var targets = new[] { ("HKCU", TestSubKey, "Mode") };
        var before = RegistryStateSnapshot.Capture(targets);

        // Mutate, then restore.
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue("Mode", 99, RegistryValueKind.DWord);

        RegistryStateSnapshot.Restore(before);

        using var check = Registry.CurrentUser.OpenSubKey(TestSubKey);
        Assert.Equal(1, (int)check!.GetValue("Mode")!);
    }

    [Fact]
    public void Restore_deletes_a_value_that_did_not_exist_at_capture()
    {
        // Ensure the value is absent at capture time.
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.DeleteValue("New", throwOnMissingValue: false);

        var targets = new[] { ("HKCU", TestSubKey, "New") };
        var before = RegistryStateSnapshot.Capture(targets);

        // Create it, then restore should remove it again.
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue("New", "hello");

        RegistryStateSnapshot.Restore(before);

        using var check = Registry.CurrentUser.OpenSubKey(TestSubKey);
        Assert.Null(check!.GetValue("New"));
    }

    [Fact]
    public void Restore_round_trips_a_string_value()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue("Name", "original", RegistryValueKind.String);

        var targets = new[] { ("HKCU", TestSubKey, "Name") };
        var before = RegistryStateSnapshot.Capture(targets);

        using (var key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            key!.SetValue("Name", "changed", RegistryValueKind.String);

        RegistryStateSnapshot.Restore(before);

        using var check = Registry.CurrentUser.OpenSubKey(TestSubKey);
        Assert.Equal("original", (string)check!.GetValue("Name")!);
    }
}
