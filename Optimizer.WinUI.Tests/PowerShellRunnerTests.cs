using System;
using System.Linq;
using System.Threading.Tasks;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for PowerShellRunner integration.
/// These tests actually launch powershell.exe and are therefore:
/// - Windows-only (appropriate for this WinUI project)
/// - Slower than unit tests
/// Mark: [Trait("Category", "Integration")]
/// </summary>
[Trait("Category", "Integration")]
public class PowerShellRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsOutput_ForSimpleScript()
    {
        var runner = new PowerShellRunner();
        var result = await runner.RunAsync("Write-Output 'hello'", timeoutMs: 10_000);

        Assert.NotNull(result);
        Assert.Contains("hello", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_OutputContainsNewlineTerminatedLines()
    {
        var runner = new PowerShellRunner();
        var result = await runner.RunAsync("Write-Output 'line1'; Write-Output 'line2'", timeoutMs: 10_000);

        Assert.NotNull(result);
        Assert.Contains("line1", result, StringComparison.Ordinal);
        Assert.Contains("line2", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReturnsNull_ForScriptWithNonZeroExit()
    {
        var runner = new PowerShellRunner();
        // exit 1 causes PowerShellRunner to return null
        var result = await runner.RunAsync("exit 1", timeoutMs: 10_000);

        Assert.Null(result);
    }

    [Fact]
    public async Task RunAsync_ReturnsNull_OnTimeout()
    {
        var runner = new PowerShellRunner();
        // Script sleeps for 10 seconds but timeout is 500 ms
        var result = await runner.RunAsync("Start-Sleep -Seconds 10", timeoutMs: 500);

        Assert.Null(result);
    }

    [Fact]
    public async Task RunAsync_NumberOutput_ReturnsStringRepresentation()
    {
        var runner = new PowerShellRunner();
        var result = await runner.RunAsync("Write-Output 42", timeoutMs: 10_000);

        Assert.NotNull(result);
        Assert.Contains("42", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_JsonOutput_CanBeDeserialized()
    {
        var runner = new PowerShellRunner();
        var result = await runner.RunAsync(
            "@{ key = 'value'; number = 99 } | ConvertTo-Json -Compress",
            timeoutMs: 10_000);

        Assert.NotNull(result);
        Assert.Contains("\"key\"", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("value", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunJsonAsync_ReturnsDeserializedObject()
    {
        var runner = new PowerShellRunner();
        var result = await runner.RunJsonAsync<SimpleDto>(
            "@{ Name = 'TestItem'; Count = 7 } | ConvertTo-Json -Compress",
            timeoutMs: 10_000);

        Assert.NotNull(result);
        Assert.Equal("TestItem", result!.Name);
        Assert.Equal(7, result.Count);
    }

    [Fact]
    public async Task RunJsonAsync_ReturnsDefault_OnMalformedJson()
    {
        var runner = new PowerShellRunner();
        // Output is not JSON
        var result = await runner.RunJsonAsync<SimpleDto>(
            "Write-Output 'not-json'",
            timeoutMs: 10_000);

        Assert.Null(result);
    }

    [Fact]
    public async Task RunAsync_ThrowingScript_ReturnsNull()
    {
        var runner = new PowerShellRunner();
        // Throws a terminating error → non-zero exit → null
        var result = await runner.RunAsync(
            "throw 'intentional error'",
            timeoutMs: 10_000);

        Assert.Null(result);
    }

    private sealed class SimpleDto
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }
}
